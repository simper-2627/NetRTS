using NetRts.PlayClient.Game;
using NetRts.Protocol;

namespace NetRts.PlayClient.Tests;

public class GameSessionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static GameSession Session(FakeServer server) =>
        new(server.Client(), server.MatchId, delay: (_, _) => Task.CompletedTask);

    private static CommandRequest Move(int unit, int x) => new() { Type = CommandType.Move, UnitIds = [unit], X = x, Y = 1 };

    [Fact]
    public async Task Plays_through_to_the_result_using_only_the_player_endpoints()
    {
        var server = new FakeServer(lastTick: 3);
        var session = Session(server);
        var run = session.RunAsync(TestContext.Current.CancellationToken);

        for (var t = 1; t <= 3; t++)
        {
            await server.Polling(t).WaitAsync(Timeout, TestContext.Current.CancellationToken);
            server.Advance(t);
        }

        await run.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(SessionPhase.Finished, session.Phase);
        Assert.NotNull(session.Result);
        Assert.Equal(3, session.State!.Tick);
        Assert.Equal(["Worker ready on tick 3", "Worker ready on tick 2", "Worker ready on tick 1"], session.Events.Select(e => e.Message));
        var id = server.MatchId;
        Assert.Contains($"GET /api/v1/matches/{id}/state?waitForTick=0", server.Requests);
        Assert.Contains($"GET /api/v1/matches/{id}/state?waitForTick=2&sinceTick=1", server.Requests);
        Assert.DoesNotContain(server.Requests, r => r.Contains("spectate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sends_at_most_one_request_per_tick_and_batches_the_rest()
    {
        var server = new FakeServer(lastTick: 5);
        var session = Session(server);
        var run = session.RunAsync(TestContext.Current.CancellationToken);
        await server.Polling(1).WaitAsync(Timeout, TestContext.Current.CancellationToken); // tick 0 is on screen

        await session.OrderAsync(Move(31, 1), "Move worker");    // goes straight out
        await session.OrderAsync(Move(31, 2), "Move worker");    // this tick's request has gone: waits
        await session.OrderAsync(Move(31, 3), "Move worker");

        Assert.Equal(1, session.Submissions);
        Assert.Equal(2, session.Orders.Count);

        server.Advance(1);
        await server.Polling(2).WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(2, session.Submissions);
        Assert.Equal(0, session.Orders.Count);
        Assert.Equal([1, 2], server.Submitted.Select(s => s.Commands.Count));
        Assert.Equal([2, 3], server.Submitted[1].Commands.Select(c => c.X!.Value));

        await session.OrderAsync(Move(31, 4), "Move worker");    // tick 1's request carried the batch: waits
        Assert.Equal(1, session.Orders.Count);

        server.Advance(2);
        await server.Polling(3).WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(3, session.Submissions);
        Assert.Equal([4], server.Submitted[2].Commands.Select(c => c.X!.Value));

        for (var t = 3; t <= 5; t++)
        {
            server.Advance(t);
        }

        await run.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Rejected_orders_are_reported_with_the_server_error()
    {
        var server = new FakeServer(lastTick: 2);
        server.RejectIndexes.Add(1);
        var session = Session(server);
        var run = session.RunAsync(TestContext.Current.CancellationToken);
        await server.Polling(1).WaitAsync(Timeout, TestContext.Current.CancellationToken);

        session.Orders.Add(Move(31, 1), "Move worker");
        await session.OrderAsync(new CommandRequest { Type = CommandType.Build, UnitIds = [31], BuildingType = BuildingType.Barracks, X = 1, Y = 1 }, "Build Barracks");

        var rejected = Assert.Single(session.Outcomes, o => !o.Accepted);
        Assert.Equal("Build Barracks", rejected.Label);
        Assert.Equal("INSUFFICIENT_RESOURCES", rejected.ErrorCode);
        Assert.Contains("costs 150", rejected.Message);
        Assert.Single(session.Outcomes, o => o.Accepted && o.Label == "Move worker");

        server.Advance(1);
        server.Advance(2);
        await run.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_dropped_long_poll_is_retried_from_the_same_tick()
    {
        var server = new FakeServer(lastTick: 2);
        server.DropOnce.Add(1);
        var session = Session(server);
        var run = session.RunAsync(TestContext.Current.CancellationToken);

        server.Advance(1);
        server.Advance(2);
        await run.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(SessionPhase.Finished, session.Phase);
        Assert.Equal(2, server.Requests.Count(r => r.EndsWith("waitForTick=1&sinceTick=0", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Orders_are_not_queued_once_the_match_is_over()
    {
        var server = new FakeServer(lastTick: 1);
        var session = Session(server);
        server.Advance(1);
        await session.RunAsync(TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);

        await session.OrderAsync(Move(31, 1), "Move worker");

        Assert.Equal(0, session.Orders.Count);
        Assert.Empty(server.Submitted);
    }
}
