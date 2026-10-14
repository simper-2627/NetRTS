using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using NetRts.Bots;
using NetRts.Protocol;

namespace NetRts.PlayClient.Tests;

/// <summary>
/// Just enough of the NetRts API for a <see cref="NetRtsClient"/>: rules, map, a long-polled state
/// that advances one tick each time the test calls <see cref="Advance"/>, command submission and the
/// result. Every request is recorded.
/// </summary>
internal sealed class FakeServer : HttpMessageHandler
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _ticks = new();
    private readonly int _lastTick;

    public FakeServer(int lastTick = 3) => _lastTick = lastTick;

    public Guid MatchId { get; } = Guid.NewGuid();

    public List<string> Requests { get; } = [];

    public List<SubmitCommandsRequest> Submitted { get; } = [];

    public List<MatchSpeedRequest> SpeedRequests { get; } = [];

    /// <summary>Indexes (within a request) of commands the server rejects with INSUFFICIENT_RESOURCES.</summary>
    public HashSet<int> RejectIndexes { get; } = [];

    /// <summary>Long-polls for these ticks fail once with a network error.</summary>
    public HashSet<int> DropOnce { get; } = [];

    public NetRtsClient Client() => new(new HttpClient(this) { BaseAddress = new Uri("http://fake/") }, "nrts_test");

    /// <summary>Lets the long-poll waiting for <paramref name="tick"/> return.</summary>
    public void Advance(int tick) => Gate(tick).TrySetResult();

    /// <summary>Completes when someone is long-polling for <paramref name="tick"/>.</summary>
    public Task Polling(int tick) => _polling.GetOrAdd(tick, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    private readonly ConcurrentDictionary<int, TaskCompletionSource> _polling = new();

    private TaskCompletionSource Gate(int tick) => _ticks.GetOrAdd(tick, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.PathAndQuery;
        lock (Requests)
        {
            Requests.Add($"{request.Method} {path}");
        }

        var match = $"/api/v1/matches/{MatchId}";
        if (path == "/api/v1/rules")
        {
            return Json(NetRts.Engine.GameRules.ToDto());
        }

        if (path == $"{match}/map")
        {
            return Json(new MapDto { Width = 4, Height = 2, Terrain = [".#..", "...."] });
        }

        if (path.StartsWith($"{match}/state", StringComparison.Ordinal))
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var wait = int.Parse(query["waitForTick"] ?? "0", System.Globalization.CultureInfo.InvariantCulture);
            if (wait > 0)
            {
                _polling.GetOrAdd(wait, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
                if (DropOnce.Remove(wait))
                {
                    throw new HttpRequestException("connection reset");
                }

                await Gate(wait).Task.WaitAsync(ct);
            }

            return Json(State(wait));
        }

        if (path == $"{match}/commands" && request.Method == HttpMethod.Post)
        {
            var body = JsonSerializer.Deserialize<SubmitCommandsRequest>(await request.Content!.ReadAsStringAsync(ct), NetRtsClient.JsonOptions)!;
            Submitted.Add(body);
            var results = body.Commands.Select((c, i) => RejectIndexes.Contains(i)
                ? new CommandResult { Index = i, Accepted = false, Error = new CommandError("INSUFFICIENT_RESOURCES", "Barracks costs 150; you have 20.") }
                : new CommandResult { Index = i, Accepted = true, CommandId = 1000 + Submitted.Count * 10 + i }).ToList();
            return Json(new SubmitCommandsResponse { Results = results, Accepted = results.Count(r => r.Accepted), Rejected = results.Count(r => !r.Accepted) });
        }

        if (path == $"{match}/speed" && request.Method == HttpMethod.Post)
        {
            var body = JsonSerializer.Deserialize<MatchSpeedRequest>(await request.Content!.ReadAsStringAsync(ct), NetRtsClient.JsonOptions)!;
            SpeedRequests.Add(body);
            if (body.TickIntervalMs is < 100)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("""{"code":"INVALID_SETTINGS","message":"tickIntervalMs must be between 100 and 10000."}""", Encoding.UTF8, "application/json"),
                };
            }

            return Json(new MatchSummaryDto { MatchId = MatchId, Paused = body.Paused ?? false, TickIntervalMs = body.TickIntervalMs ?? 500, SpeedAdjustable = true });
        }

        if (path == $"{match}/result")
        {
            return Json(new MatchResultDto { MatchId = MatchId, Outcome = new MatchOutcomeDto { Reason = MatchEndReason.TimeLimit, Ticks = _lastTick } });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"code":"NOT_FOUND","message":"No such endpoint."}""", Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>Changes the state the server hands out, e.g. to add units.</summary>
    public Func<GameStateDto, GameStateDto> Shape { get; set; } = s => s;

    private GameStateDto State(int tick) => Shape(StateAdapterTests.SampleState(tick)) with
    {
        MatchId = MatchId,
        Status = tick >= _lastTick ? MatchStatus.Completed : MatchStatus.Active,
        Events = tick == 0 ? [] : [new GameEventDto { Tick = tick, Kind = "UnitProduced", Message = $"Worker ready on tick {tick}" }],
    };

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, NetRtsClient.JsonOptions), Encoding.UTF8, "application/json"),
    };
}
