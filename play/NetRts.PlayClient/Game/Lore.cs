using NetRts.Protocol;

namespace NetRts.PlayClient.Game;

/// <summary>
/// Display names in the active brand (the original NetRTS look or the Snow College theme), read once
/// from the shared <c>lore.js</c> so the HUD says what the map and the spectator site say. The API
/// keeps its own names; this only changes labels.
/// </summary>
public sealed class Lore
{
    /// <summary>The original brand: API names, spaced out.</summary>
    public static readonly Lore Classic = new(new LoreTables());

    private readonly LoreTables _t;
    private readonly Dictionary<string, string> _retold = [];

    public Lore(LoreTables tables) => _t = tables;

    public bool Snow => _t.Snow;

    public string Game => _t.Game ?? "NetRTS";

    /// <summary>"ore", or "grubs".</summary>
    public string Ore => _t.Ore ?? "ore";

    public string Unit(UnitType type) => type.ToString();

    public string Building(BuildingType type, int? owner = null)
    {
        if (!Snow)
        {
            return Spaced(type.ToString());
        }

        return owner switch
        {
            { } slot when type == BuildingType.CommandCenter && _t.HqNames.Count > 0 => BySlot(_t.HqNames, slot),
            { } slot when type == BuildingType.Barracks && _t.HousingNames.Count > 0 => BySlot(_t.HousingNames, slot),
            _ => _t.BuildingNames.GetValueOrDefault(type.ToString(), type.ToString()),
        };
    }

    public string Upgrade(UpgradeType type) =>
        Snow ? _t.UpgradeNames.GetValueOrDefault(type.ToString(), type.ToString()) : Spaced(type.ToString());

    /// <summary>"house-rusher" → "rusher" (or its Snow nickname); other names as they are.</summary>
    public string Player(string name)
    {
        if (!name.StartsWith("house-", StringComparison.Ordinal))
        {
            return name;
        }

        var parts = name[6..].Split(' ', 2);
        var bot = _t.BotNicknames.GetValueOrDefault(parts[0], parts[0]);
        return parts.Length > 1 ? $"{bot} {parts[1]}" : bot;
    }

    /// <summary>An engine message in this brand's words, once <see cref="Learn"/> has been told it; as it is until then.</summary>
    public string Retell(string message) => _retold.GetValueOrDefault(message, message);

    /// <summary>Messages not retold yet.</summary>
    public IEnumerable<string> Unknown(IEnumerable<string> messages) => messages.Where(m => !_retold.ContainsKey(m)).Distinct();

    public void Learn(IReadOnlyList<string> messages, IReadOnlyList<string> retold)
    {
        for (var i = 0; i < messages.Count && i < retold.Count; i++)
        {
            _retold[messages[i]] = retold[i];
        }
    }

    /// <summary>"CommandCenter" → "Command Center".</summary>
    public static string Spaced(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, "([a-z])([A-Z0-9])", "$1 $2");

    private static string BySlot(IReadOnlyList<string> list, int slot) => list[((slot % list.Count) + list.Count) % list.Count];
}

/// <summary>The tables <c>playview.js</c> hands over from <c>lore.js</c>.</summary>
public sealed record LoreTables
{
    public bool Snow { get; init; }
    public string? Game { get; init; }
    public string? Ore { get; init; }
    public Dictionary<string, string> BuildingNames { get; init; } = [];
    public Dictionary<string, string> UpgradeNames { get; init; } = [];
    public IReadOnlyList<string> HqNames { get; init; } = [];
    public IReadOnlyList<string> HousingNames { get; init; } = [];
    public Dictionary<string, string> BotNicknames { get; init; } = [];
}
