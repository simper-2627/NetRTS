using NetRts.PlayClient.Game;
using NetRts.Protocol;

namespace NetRts.PlayClient.Tests;

public class LobbyTests
{
    [Theory]
    [InlineData("http://localhost:5080", "http://localhost:5080")]
    [InlineData("localhost:5080/", "http://localhost:5080")]
    [InlineData("  https://netrts.example.edu/  ", "https://netrts.example.edu")]
    [InlineData("http://10.0.0.5:5080/?x=1#y", "http://10.0.0.5:5080")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("ftp://example.com", null)]
    public void Server_addresses_are_normalized(string input, string? expected) =>
        Assert.Equal(expected, ClientSettings.NormalizeServer(input));

    [Fact]
    public void Playing_a_house_bot_fills_the_second_seat_with_it()
    {
        var request = new MatchSetup { Opponent = "sitter", TickIntervalMs = 500, MapSize = 48, Seed = 7 }.ToRequest();

        Assert.Equal(2, request.MaxPlayers);
        Assert.Equal(["sitter"], request.HouseBots);
        Assert.Equal(500, request.Settings!.TickIntervalMs);
        Assert.Equal(48, request.Settings.MapWidth);
        Assert.Equal(48, request.Settings.MapHeight);
        Assert.Equal(7, request.Settings.Seed);
        Assert.Null(request.Settings.MaxTicks);
        Assert.Null(request.Name);
    }

    [Fact]
    public void An_open_match_has_no_house_bots_and_keeps_its_name()
    {
        var request = new MatchSetup { Opponent = null, Players = 3, Name = "  lab-table-3 ", MaxTicks = 600 }.ToRequest();

        Assert.Equal(3, request.MaxPlayers);
        Assert.Null(request.HouseBots);
        Assert.Equal("lab-table-3", request.Name);
        Assert.Equal(600, request.Settings!.MaxTicks);
        Assert.Null(request.Settings.MapWidth); // default size: the server grows it with the player count
    }

    [Fact]
    public void BotRunner_command_joins_by_name_when_there_is_one()
    {
        var id = Guid.NewGuid();
        var named = new MatchSummaryDto { MatchId = id, Name = "lab-table-3" };
        var unnamed = new MatchSummaryDto { MatchId = id };

        Assert.Equal(
            "dotnet run --project src/NetRts.BotRunner -- --server http://localhost:5080 --name <botname> --strategy balanced --join lab-table-3",
            MatchSetup.BotRunnerCommand("http://localhost:5080", named));
        Assert.EndsWith($"--join {id}", MatchSetup.BotRunnerCommand("http://localhost:5080", unnamed));
    }
}
