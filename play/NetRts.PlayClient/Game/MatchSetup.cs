using NetRts.Protocol;

namespace NetRts.PlayClient.Game;

/// <summary>The lobby's "new match" form, turned into the API request it sends.</summary>
public sealed class MatchSetup
{
    /// <summary>Speeds offered in the lobby; the server allows 100-10000 ms.</summary>
    public static readonly int[] TickChoices = [250, 500, 1000, 2000];

    /// <summary>Map sizes offered; 0 lets the server pick (64×64 for up to four players).</summary>
    public static readonly int[] MapChoices = [0, 48, 64, 80, 96];

    /// <summary>The house bot to play against, or null for an open match that waits for other players.</summary>
    public string? Opponent { get; set; } = "sitter";

    /// <summary>Seats in an open match (you plus this many minus one others).</summary>
    public int Players { get; set; } = 2;

    /// <summary>Optional name so others can find an open match, e.g. "lab-table-3".</summary>
    public string? Name { get; set; }

    public int TickIntervalMs { get; set; } = 1000;

    public int MapSize { get; set; }

    public int? MaxTicks { get; set; }

    public int? Seed { get; set; }

    public CreateMatchRequest ToRequest()
    {
        var settings = new MatchSettingsDto
        {
            TickIntervalMs = TickIntervalMs,
            MapWidth = MapSize > 0 ? MapSize : null,
            MapHeight = MapSize > 0 ? MapSize : null,
            MaxTicks = MaxTicks,
            Seed = Seed,
        };
        var name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim();
        return Opponent is { } bot
            ? new CreateMatchRequest { MaxPlayers = 2, HouseBots = [bot], Settings = settings, Name = name }
            : new CreateMatchRequest { MaxPlayers = Math.Clamp(Players, 2, 16), Settings = settings, Name = name };
    }

    /// <summary>The command that seats a house-bot strategy in an open match through the BotRunner, from the repo root.</summary>
    public static string BotRunnerCommand(string server, MatchSummaryDto match, string botName = "<botname>", string strategy = "balanced") =>
        $"dotnet run --project src/NetRts.BotRunner -- --server {server} --name {botName} --strategy {strategy} --join {match.Name ?? match.MatchId.ToString()}";
}
