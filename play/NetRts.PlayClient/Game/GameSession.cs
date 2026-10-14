using System.Net;
using NetRts.Bots;
using NetRts.Protocol;

namespace NetRts.PlayClient.Game;

public enum SessionPhase
{
    Connecting,
    Waiting,
    Playing,
    Finished,
    Failed,
}

/// <summary>
/// One person playing one match, through the same calls <see cref="BotLoop"/> makes: wait for the
/// start, read the map and rules, then long-poll <c>state?waitForTick=N+1&amp;sinceTick=N</c> for
/// every tick. Orders queue locally and go out in one <c>POST /commands</c> per tick, sent as soon as
/// there is something to send. Never touches the spectator endpoints.
/// </summary>
public sealed class GameSession
{
    public const int MaxEvents = 200;
    public const int MaxOutcomes = 30;

    private readonly NetRtsClient _client;
    private readonly List<GameEventDto> _events = [];
    private readonly List<OrderOutcome> _outcomes = [];
    private readonly Dictionary<long, string> _labels = [];
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private CancellationToken _ct;
    private bool _sending;
    private int _sentForTick = -1;

    public GameSession(NetRtsClient client, Guid matchId, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _client = client;
        MatchId = matchId;
        _delay = delay ?? Task.Delay;
    }

    public Guid MatchId { get; }

    public SessionPhase Phase { get; private set; } = SessionPhase.Connecting;

    /// <summary>Why the session is stuck or retrying, for the page; null while all is well.</summary>
    public string? Problem { get; private set; }

    public RulesDto? Rules { get; private set; }

    public MapDto? Map { get; private set; }

    public GameStateDto? State { get; private set; }

    public RenderState? Render { get; private set; }

    public MatchResultDto? Result { get; private set; }

    public OrderQueue Orders { get; } = new();

    /// <summary>The player's events, newest first.</summary>
    public IReadOnlyList<GameEventDto> Events => _events;

    /// <summary>Recent order results, newest first.</summary>
    public IReadOnlyList<OrderOutcome> Outcomes => _outcomes;

    /// <summary>Number of <c>POST /commands</c> requests made, for the HUD and tests.</summary>
    public int Submissions { get; private set; }

    /// <summary>Raised after every change worth redrawing: a new tick, an order result, a problem.</summary>
    public event Func<Task>? Updated;

    public async Task RunAsync(CancellationToken ct)
    {
        _ct = ct;
        Rules = await Retry(() => _client.GetRulesAsync(ct), ct);
        Phase = SessionPhase.Waiting;
        await Notify();

        // Long-polls with waitForTick=0 until every seat is filled and the match has started.
        var state = await Retry(() => _client.WaitForStartAsync(MatchId, ct), ct);
        Map = await Retry(() => _client.GetMapAsync(MatchId, ct), ct);
        Phase = SessionPhase.Playing;
        await Accept(state);

        while (state.Status != MatchStatus.Completed && !ct.IsCancellationRequested)
        {
            var last = state.Tick;
            state = await Retry(() => _client.GetStateAsync(MatchId, waitForTick: last + 1, sinceTick: last, ct: ct), ct);
            await Accept(state);
        }

        Phase = SessionPhase.Finished;
        Orders.Clear();
        await Notify();
        for (var attempt = 0; attempt < 10 && Result is null && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                Result = await _client.GetResultAsync(MatchId, ct);
            }
            catch (NetRtsApiException e) when (e.Status == HttpStatusCode.Conflict)
            {
                await _delay(TimeSpan.FromMilliseconds(500), ct); // the result is written just after the last tick
            }
        }

        await Notify();
    }

    /// <summary>Queues an order and sends it straight away unless this tick's request has already gone.</summary>
    public Task OrderAsync(CommandRequest command, string label) => OrderAsync([new Order(command, label)]);

    /// <summary>Queues orders that belong together (so they travel in the same request) and sends them like <see cref="OrderAsync(CommandRequest, string)"/>.</summary>
    public async Task OrderAsync(IReadOnlyList<Order> orders)
    {
        if (Phase != SessionPhase.Playing || orders.Count == 0)
        {
            return;
        }

        foreach (var order in orders)
        {
            Orders.Add(order.Command, order.Label);
        }

        await Notify();
        await SendAsync();
    }

    private async Task Accept(GameStateDto state)
    {
        var isNew = State is null || state.Tick != State.Tick;
        State = state;
        Render = StateAdapter.ToRenderState(state);
        if (isNew)
        {
            AddEvents(state.Events);
        }

        await Notify();
        await SendAsync();
    }

    private void AddEvents(IReadOnlyList<GameEventDto> events)
    {
        foreach (var e in events)
        {
            if (_events.Any(x => x.Tick == e.Tick && x.Kind == e.Kind && x.Message == e.Message && x.EntityId == e.EntityId))
            {
                continue;
            }

            _events.Insert(0, e);
            if (e.Kind == "CommandFailed" && e.CommandId is { } id && _labels.TryGetValue(id, out var label))
            {
                AddOutcome(new OrderOutcome(e.Tick, label, false, id, "CommandFailed", e.Message));
            }
        }

        if (_events.Count > MaxEvents)
        {
            _events.RemoveRange(MaxEvents, _events.Count - MaxEvents);
        }
    }

    /// <summary>One request per tick: the orders queued so far, if this tick hasn't had its request yet.</summary>
    private async Task SendAsync()
    {
        if (_sending || Orders.Count == 0 || State is not { Status: MatchStatus.Active } state || state.Tick == _sentForTick)
        {
            return;
        }

        _sending = true;
        _sentForTick = state.Tick;
        var batch = Orders.TakeBatch();
        try
        {
            Submissions++;
            var response = await _client.SubmitAsync(MatchId, batch.Select(o => o.Command).ToList(), _ct);
            foreach (var r in response.Results)
            {
                var label = r.Index < batch.Count ? batch[r.Index].Label : "Order";
                if (r.CommandId is { } id)
                {
                    _labels[id] = label;
                }

                AddOutcome(new OrderOutcome(state.Tick, label, r.Accepted, r.CommandId, r.Error?.Code, r.Error?.Message));
            }

            Problem = null;
        }
        catch (NetRtsApiException e) when (e.Status == HttpStatusCode.Conflict)
        {
            // The match ended (or hasn't started) between the click and the request: nothing to retry.
            Problem = ApiErrors.Describe(e, "");
        }
        catch (Exception e) when (e is HttpRequestException or NetRtsApiException or TaskCanceledException && !_ct.IsCancellationRequested)
        {
            Orders.Requeue(batch);
            _sentForTick = -1; // try again with the next tick
            Problem = $"Orders not sent: {ApiErrors.Describe(e, "the server")}";
        }
        finally
        {
            _sending = false;
        }

        await Notify();
    }

    private void AddOutcome(OrderOutcome outcome)
    {
        _outcomes.Insert(0, outcome);
        if (_outcomes.Count > MaxOutcomes)
        {
            _outcomes.RemoveRange(MaxOutcomes, _outcomes.Count - MaxOutcomes);
        }
    }

    /// <summary>Retries network trouble (with a growing pause) and rate limiting; anything else ends the session.</summary>
    private async Task<T> Retry<T>(Func<Task<T>> call, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var value = await call();
                if (Problem is not null && !_sending)
                {
                    Problem = null;
                }

                return value;
            }
            catch (NetRtsApiException e) when (e.Status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                Phase = SessionPhase.Failed;
                Problem = ApiErrors.Describe(e, "the server");
                await Notify();
                throw;
            }
            catch (Exception e) when (e is HttpRequestException or NetRtsApiException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                Problem = $"{ApiErrors.Describe(e, "the server")} Retrying…";
                await Notify();
                await _delay(TimeSpan.FromSeconds(Math.Min(5, attempt)), ct);
            }
        }
    }

    private async Task Notify()
    {
        if (Updated is { } handler)
        {
            await handler();
        }
    }
}
