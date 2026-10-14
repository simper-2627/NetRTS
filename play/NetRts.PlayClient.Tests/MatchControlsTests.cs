using NetRts.Bots;
using NetRts.PlayClient.Game;

namespace NetRts.PlayClient.Tests;

public class MatchControlsTests
{
    private static (FakeServer Server, MatchControls Controls) Create()
    {
        var server = new FakeServer();
        return (server, new MatchControls(new HttpClient(server) { BaseAddress = new Uri("http://fake/") }, server.MatchId));
    }

    [Fact]
    public async Task Pause_and_speed_go_to_the_speed_endpoint()
    {
        var (server, controls) = Create();

        var paused = await controls.SetPausedAsync(true, TestContext.Current.CancellationToken);
        var faster = await controls.SetSpeedAsync(250, TestContext.Current.CancellationToken);

        Assert.True(paused.Paused);
        Assert.Equal(250, faster.TickIntervalMs);
        Assert.Equal([(true, (int?)null), (null, 250)], server.SpeedRequests.Select(r => (r.Paused, r.TickIntervalMs)));
    }

    [Fact]
    public async Task Server_errors_come_back_as_api_exceptions()
    {
        var (_, controls) = Create();

        var e = await Assert.ThrowsAsync<NetRtsApiException>(() => controls.SetSpeedAsync(10, TestContext.Current.CancellationToken));

        Assert.Equal("INVALID_SETTINGS", e.Code);
    }
}
