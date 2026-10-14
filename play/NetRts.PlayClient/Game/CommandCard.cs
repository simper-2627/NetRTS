using NetRts.Protocol;

namespace NetRts.PlayClient.Game;

/// <summary>One button on the command card: what it does, its hotkey, and why it's unavailable (null if it isn't).</summary>
public sealed record CardButton(CardAction Action, char Hotkey, int Cost, string? Blocked);

/// <summary>A command-card action.</summary>
public abstract record CardAction;

public sealed record BuildAction(BuildingType Type) : CardAction;

public sealed record ProduceAction(UnitType Type) : CardAction;

public sealed record ResearchAction(UpgradeType Upgrade) : CardAction;

/// <summary>
/// The buttons the selection offers, with costs and requirements from <c>GET /rules</c>, checked
/// against the current state so obviously impossible orders are refused here instead of by the
/// server. The server still has the last word.
/// </summary>
public static class CommandCard
{
    public static readonly IReadOnlyDictionary<BuildingType, char> BuildKeys = new Dictionary<BuildingType, char>
    {
        [BuildingType.CommandCenter] = 'C',
        [BuildingType.Barracks] = 'B',
        [BuildingType.ResourceDepot] = 'D',
        [BuildingType.TechLab] = 'T',
        [BuildingType.GuardTower] = 'G',
    };

    public static readonly IReadOnlyDictionary<UnitType, char> ProduceKeys = new Dictionary<UnitType, char>
    {
        [UnitType.Worker] = 'W',
        [UnitType.Soldier] = 'S',
        [UnitType.Archer] = 'A',
        [UnitType.Scout] = 'C',
    };

    /// <summary>Each upgrade line has one key; it researches the next tier.</summary>
    public static readonly IReadOnlyDictionary<string, char> ResearchKeys = new Dictionary<string, char>
    {
        ["Weapons"] = 'W',
        ["Armor"] = 'A',
        ["Mobility"] = 'M',
        ["Harvesting"] = 'H',
    };

    /// <summary>Workers selected: a Build button for each building type.</summary>
    public static IReadOnlyList<CardButton> BuildButtons(GameStateDto state, RulesDto rules, int budget, Lore? lore = null) =>
        rules.Buildings.Select(b => new CardButton(
            new BuildAction(b.Type), BuildKeys.GetValueOrDefault(b.Type, '?'), b.Cost, Placement.GlobalProblem(state, rules, b.Type, budget, lore))).ToList();

    /// <summary>One of your buildings selected: what it trains, and (a TechLab) what it can research.</summary>
    public static IReadOnlyList<CardButton> BuildingButtons(GameStateDto state, RulesDto rules, BuildingDto building, int budget, Lore? lore = null)
    {
        lore ??= Lore.Classic;
        var stats = rules.Buildings.FirstOrDefault(b => b.Type == building.Type);
        if (stats is null || building.Owner != state.You.Slot)
        {
            return [];
        }

        var buttons = new List<CardButton>();
        var queueFull = building.Production.Count >= rules.Economy.MaxProductionQueue;
        foreach (var unit in stats.Produces)
        {
            var cost = rules.Units.FirstOrDefault(u => u.Type == unit)?.Cost ?? 0;
            var blocked = !building.Completed ? "Still under construction"
                : queueFull ? $"Queue full ({rules.Economy.MaxProductionQueue})"
                : budget < cost ? $"Not enough {lore.Ore}" : null;
            buttons.Add(new CardButton(new ProduceAction(unit), ProduceKeys.GetValueOrDefault(unit, '?'), cost, blocked));
        }

        if (stats.CanResearch)
        {
            foreach (var line in ResearchKeys)
            {
                if (NextTier(state, rules, line.Key) is not { } upgrade)
                {
                    continue; // both tiers done
                }

                var blocked = !building.Completed ? "Still under construction"
                    : building.Research is not null ? "Already researching here"
                    : state.You.Researching.Any(r => r.Upgrade == upgrade.Type) ? "Being researched elsewhere"
                    : upgrade.Requires is { } before && !state.You.Upgrades.Contains(before) ? $"Needs {lore.Upgrade(before)}"
                    : budget < upgrade.Cost ? $"Not enough {lore.Ore}" : null;
                buttons.Add(new CardButton(new ResearchAction(upgrade.Type), line.Value, upgrade.Cost, blocked));
            }
        }

        return buttons;
    }

    /// <summary>The first tier of an upgrade line (e.g. "Weapons") that you don't have yet.</summary>
    public static UpgradeStatsDto? NextTier(GameStateDto state, RulesDto rules, string line) =>
        rules.Upgrades.Where(u => u.Type.ToString().StartsWith(line, StringComparison.Ordinal) && !state.You.Upgrades.Contains(u.Type))
            .OrderBy(u => u.Type)
            .FirstOrDefault();

    /// <summary>The button a key presses, if any (case-insensitive).</summary>
    public static CardButton? ForKey(IEnumerable<CardButton> buttons, char key) =>
        buttons.FirstOrDefault(b => char.ToUpperInvariant(b.Hotkey) == char.ToUpperInvariant(key));
}

/// <summary>Where a building may go, as far as this player can know (the server checks again).</summary>
public static class Placement
{
    /// <summary>Why <paramref name="type"/> can't be placed at (x, y), or null if it can.</summary>
    public static string? Problem(GameStateDto state, MapDto map, DepositMemory deposits, RulesDto rules, BuildingType type, int x, int y, int budget, Lore? lore = null) =>
        TileProblem(state, map, deposits, type, x, y) ?? GlobalProblem(state, rules, type, budget, lore);

    /// <summary>
    /// The whole map at once for the build ghost: one string per row, '1' where the building may go,
    /// '0' where it may not. Sent to the JS view when placement starts and after each tick.
    /// </summary>
    public static IReadOnlyList<string> Mask(GameStateDto state, MapDto map, DepositMemory deposits, RulesDto rules, BuildingType type, int budget)
    {
        var anywhere = GlobalProblem(state, rules, type, budget) is null;
        var rows = new string[map.Height];
        var row = new char[map.Width];
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                row[x] = anywhere && TileProblem(state, map, deposits, type, x, y) is null ? '1' : '0';
            }

            rows[y] = new string(row);
        }

        return rows;
    }

    /// <summary>What rules out this building everywhere: a missing prerequisite or too little ore.</summary>
    public static string? GlobalProblem(GameStateDto state, RulesDto rules, BuildingType type, int budget, Lore? lore = null)
    {
        lore ??= Lore.Classic;
        var stats = rules.Buildings.FirstOrDefault(b => b.Type == type);
        if (stats is null)
        {
            return null;
        }

        var me = state.You.Slot;
        foreach (var required in stats.Requires)
        {
            if (!state.Buildings.Any(b => b.Owner == me && b.Type == required && b.Completed))
            {
                return $"Needs a finished {lore.Building(required)}";
            }
        }

        return budget < stats.Cost ? $"Not enough {lore.Ore}" : null;
    }

    /// <summary>What rules out this tile: off the map, rock, a deposit, a building, or a visible enemy on it.</summary>
    public static string? TileProblem(GameStateDto state, MapDto map, DepositMemory deposits, BuildingType type, int x, int y)
    {
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
        {
            return "Off the map";
        }

        if (map.Terrain[y][x] != '.')
        {
            return "Rock";
        }

        if (deposits.At(x, y) is not null || state.Resources.Any(r => r.X == x && r.Y == y))
        {
            return "There's a deposit there";
        }

        var me = state.You.Slot;
        if (state.Buildings.FirstOrDefault(b => b.X == x && b.Y == y) is { } there)
        {
            // Workers sent to your own unfinished site of the same type help build it.
            return there.Owner == me && there.Type == type && !there.Completed ? null : "There's a building there";
        }

        if (state.RememberedBuildings.Any(b => b.X == x && b.Y == y))
        {
            return "An enemy building was there";
        }

        return state.Units.Any(u => u.Owner != me && u.X == x && u.Y == y) ? "An enemy unit is standing there" : null;
    }
}
