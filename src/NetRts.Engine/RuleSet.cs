using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using NetRts.Protocol;

namespace NetRts.Engine;

/// <summary>
/// A complete, validated set of rules: units, buildings, upgrades and economy, as read from a rules file
/// such as <c>src/NetRts.Engine/rules.json</c>. The names in the file are the type names bots use.
/// </summary>
public sealed class RuleSet
{
    public const string FileName = "rules.json";

    private static readonly JsonSerializerOptions FileOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
    };

    private RuleSet(RulesFile file)
    {
        var errors = new List<string>();
        Check(file, errors);
        var upgrades = file.Upgrades.ToDictionary(kv => kv.Key, kv => Normalize($"upgrades.{kv.Key}", kv.Value, file, errors));
        if (errors.Count > 0)
        {
            throw new InvalidDataException("Invalid rules:" + string.Concat(errors.Select(e => $"{Environment.NewLine}  {e}")));
        }

        Units = file.Units;
        Buildings = file.Buildings.ToDictionary(
            b => b.Key,
            b => b.Value with { Produces = file.Units.Where(u => u.Value.ProducedAt == b.Key).Select(u => u.Key).ToList() });
        Upgrades = upgrades;
        Economy = file.Economy;
        Serum = file.Serum;
        StartingWorkers = Economy.StartingUnits.Where(s => Units[s.Key].IsWorker).Sum(s => s.Value);
        Version = $"{GameSimulation.Revision}-{Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(file, FileOptions)))[..12]}";
    }

    public IReadOnlyDictionary<UnitType, UnitStats> Units { get; }
    public IReadOnlyDictionary<BuildingType, BuildingStats> Buildings { get; }
    public IReadOnlyDictionary<UpgradeType, UpgradeStats> Upgrades { get; }
    public EconomyRules Economy { get; }
    public SerumRules Serum { get; }

    /// <summary>How many of the starting units are workers.</summary>
    public int StartingWorkers { get; }

    /// <summary>
    /// Identifies these rules and this simulation, e.g. "1-3f9a0c2b7d41". Recorded with every match: a
    /// replay re-simulated under a different version may not play out the way the match did.
    /// </summary>
    public string Version { get; }

    /// <summary>Reads a rules file; throws with every problem in it if it isn't valid.</summary>
    public static RuleSet Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The rules file {path} is missing.", path);
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{path}: {e.Message}", e);
        }
    }

    /// <summary>Reads rules from JSON in the rules file format; throws with every problem in it if it isn't valid.</summary>
    public static RuleSet Parse(string json)
    {
        RulesFile? file;
        try
        {
            file = JsonSerializer.Deserialize<RulesFile>(json, FileOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException(e.Message, e);
        }

        return new RuleSet(file ?? throw new InvalidDataException("The rules file is empty."));
    }

    /// <summary><paramref name="stats"/> with an upgrade's changes for that unit type applied.</summary>
    internal static UnitStats Apply(UnitStats stats, IReadOnlyDictionary<string, int> changes) => Merge(stats, changes);

    /// <summary><paramref name="stats"/> with an upgrade's changes for that building type applied.</summary>
    internal static BuildingStats Apply(BuildingStats stats, IReadOnlyDictionary<string, int> changes) =>
        Merge(stats, changes) with { Produces = stats.Produces };

    public RulesDto ToDto() => new()
    {
        Units = Units.Select(kv => ToDto(kv.Key, kv.Value)).ToList(),
        Buildings = Buildings.Select(kv => ToDto(kv.Key, kv.Value)).ToList(),
        Upgrades = Upgrades.Select(kv => new UpgradeStatsDto
        {
            Type = kv.Key,
            Cost = kv.Value.Cost,
            ResearchTicks = kv.Value.ResearchTicks,
            Requires = kv.Value.Requires,
            Effect = kv.Value.Effect,
            Units = kv.Value.Units,
            Buildings = kv.Value.Buildings,
        }).ToList(),
        Economy = new EconomyRulesDto
        {
            StartingResources = Economy.StartingResources,
            StartingWorkers = StartingWorkers,
            WorkerCarryCapacity = Units.Values.FirstOrDefault(u => u.IsWorker)?.CarryCapacity ?? 0,
            GatherPerTick = Units.Values.FirstOrDefault(u => u.IsWorker)?.GatherPerTick ?? 0,
            MaxProductionQueue = Economy.MaxProductionQueue,
            StartingBuilding = Economy.StartingBuilding,
            StartingUnits = Economy.StartingUnits,
        },
        Notes =
        [
            "Coordinates are (x, y) with (0, 0) at the top-left. tile = y * mapWidth + x.",
            "Attack range and adjacency use Chebyshev distance (diagonals count as 1). Vision uses Euclidean distance.",
            "Units may share tiles. Buildings, resource deposits and rock block movement.",
            "Damage dealt = max(1, attacker damage - target armor). All attacks in a tick land simultaneously.",
            "Unit costs are paid when production is queued; building costs when the first worker starts construction.",
            "Ore beyond your storage capacity is lost. Storage = sum of StorageBonus of your completed buildings.",
            "Siege units (ShotTicks > 0) lob shells at a tile: they land ShotTicks later and hit every enemy on that tile then. They can't fire inside MinRange.",
            "Idle combat units fight back against enemies already in range. Attack-move (Attack with a position) engages anything seen en route.",
            "Upgrades set the stats they list for your units and buildings of those types, existing and future; HP keeps the same share of max HP. "
            + "If two upgrades set the same stat, the one researched last wins. you.unitStats and you.buildingStats in your state are your current values.",
            "A player with no headquarters (IsHeadquarters, e.g. the Command Center), finished or under construction, is eliminated.",
            "At the tick limit the highest total score wins; equal scores are a draw.",
            "Score: destruction (value of enemy assets destroyed) + economy (ore banked) + survival (value of your living units and finished buildings).",
        ],
    };

    internal static UnitStatsDto ToDto(UnitType type, UnitStats s) => new()
    {
        Type = type,
        Cost = s.Cost,
        BuildTicks = s.BuildTicks,
        MaxHp = s.MaxHp,
        Damage = s.Damage,
        Armor = s.Armor,
        Range = s.Range,
        AttackCooldown = s.AttackCooldown,
        Vision = s.Vision,
        Speed = s.Speed,
        ProducedAt = s.ProducedAt,
        MinRange = s.MinRange,
        ShotTicks = s.ShotTicks,
        IsWorker = s.IsWorker,
        CarryCapacity = s.CarryCapacity,
        GatherPerTick = s.GatherPerTick,
    };

    internal static BuildingStatsDto ToDto(BuildingType type, BuildingStats s) => new()
    {
        Type = type,
        Cost = s.Cost,
        BuildWork = s.BuildWork,
        MaxHp = s.MaxHp,
        Armor = s.Armor,
        Vision = s.Vision,
        Damage = s.Damage,
        Range = s.Range,
        AttackCooldown = s.AttackCooldown,
        StorageBonus = s.StorageBonus,
        IsDropOff = s.IsDropOff,
        Produces = s.Produces,
        CanResearch = s.CanResearch,
        Requires = s.Requires,
        RequiresUpgrade = s.RequiresUpgrade,
        IsHeadquarters = s.IsHeadquarters,
    };

    private static T Merge<T>(T stats, IReadOnlyDictionary<string, int> changes)
    {
        if (changes.Count == 0)
        {
            return stats;
        }

        var node = Fields(stats);
        foreach (var (stat, value) in changes)
        {
            node[stat] = value;
        }

        return node.Deserialize<T>(FileOptions)!;
    }

    private static JsonObject Fields<T>(T stats) => JsonSerializer.SerializeToNode(stats, FileOptions)!.AsObject();

    private static bool IsNumber(JsonNode? node) => node?.GetValueKind() == JsonValueKind.Number;

    private static void Check(RulesFile file, List<string> errors)
    {
        // References must use a name exactly as it is declared, so what bots see is consistent.
        void Exists<T>(IEnumerable<T> declared, T name, string at, string what)
            where T : struct, ITypeId<T>
        {
            if (!declared.Any(d => string.Equals(d.Name, name.Name, StringComparison.Ordinal)))
            {
                var near = declared.FirstOrDefault(d => d.Equals(name));
                errors.Add($"{at}: there is no {what} \"{name}\"{(near.Name is null ? "" : $" (did you mean \"{near}\"?)")}.");
            }
        }

        foreach (var (type, unit) in file.Units)
        {
            CheckValues($"units.{type}", unit, AtLeastOne(unit), errors);
            if (unit.ProducedAt is { } at)
            {
                Exists(file.Buildings.Keys, at, $"units.{type}.producedAt", "building");
            }
        }

        foreach (var (type, building) in file.Buildings)
        {
            CheckValues($"buildings.{type}", building, ["buildWork", "maxHp"], errors);
            foreach (var required in building.Requires)
            {
                Exists(file.Buildings.Keys, required, $"buildings.{type}.requires", "building");
            }

            if (building.RequiresUpgrade is { } upgrade)
            {
                Exists(file.Upgrades.Keys, upgrade, $"buildings.{type}.requiresUpgrade", "upgrade");
            }
        }

        foreach (var (type, upgrade) in file.Upgrades)
        {
            if (upgrade.Cost < 0 || upgrade.ResearchTicks < 1)
            {
                errors.Add($"upgrades.{type}: cost must be 0 or more and researchTicks at least 1.");
            }

            if (upgrade.Requires is { } before)
            {
                Exists(file.Upgrades.Keys, before, $"upgrades.{type}.requires", "upgrade");
            }

            foreach (var unit in upgrade.Units.Keys)
            {
                Exists(file.Units.Keys, unit, $"upgrades.{type}.units", "unit");
            }

            foreach (var building in upgrade.Buildings.Keys)
            {
                Exists(file.Buildings.Keys, building, $"upgrades.{type}.buildings", "building");
            }
        }

        var economy = file.Economy;
        Exists(file.Buildings.Keys, economy.StartingBuilding, "economy.startingBuilding", "building");
        if (file.Buildings.TryGetValue(economy.StartingBuilding, out var start) && !start.IsHeadquarters)
        {
            errors.Add($"economy.startingBuilding: {economy.StartingBuilding} must have isHeadquarters, or every player is out on the first tick.");
        }

        foreach (var (unit, count) in economy.StartingUnits)
        {
            Exists(file.Units.Keys, unit, "economy.startingUnits", "unit");
            if (count < 0)
            {
                errors.Add($"economy.startingUnits.{unit} must be 0 or more.");
            }
        }

        CheckValues("economy", economy, ["maxProductionQueue"], errors);
        CheckValues("serum", file.Serum, ["factor"], errors);
    }

    /// <summary>The unit stats that can't be 0: a worker that carries or mines nothing would never finish a trip.</summary>
    private static string[] AtLeastOne(UnitStats unit) =>
        unit.IsWorker ? ["buildTicks", "maxHp", "carryCapacity", "gatherPerTick"] : ["buildTicks", "maxHp"];

    /// <summary>
    /// Every number in <paramref name="stats"/> (or just the <paramref name="only"/> ones) must be 0 or more,
    /// and the <paramref name="positive"/> ones at least 1.
    /// </summary>
    private static void CheckValues<T>(string at, T stats, string[] positive, List<string> errors, IEnumerable<string>? only = null)
    {
        foreach (var (stat, value) in Fields(stats).Where(f => only?.Contains(f.Key) != false))
        {
            if (IsNumber(value) && value!.GetValue<int>() is var n && (n < 0 || (n == 0 && positive.Contains(stat))))
            {
                errors.Add($"{at}.{stat} is {n}; it must be {(positive.Contains(stat) ? "at least 1" : "0 or more")}.");
            }
        }
    }

    /// <summary>The upgrade with its stat names spelled as in the stats (camelCase) and an effect text, checking what it would do to each type.</summary>
    private static UpgradeStats Normalize(string at, UpgradeStats upgrade, RulesFile file, List<string> errors)
    {
        var units = new Dictionary<UnitType, IReadOnlyDictionary<string, int>>();
        foreach (var (type, changes) in upgrade.Units)
        {
            if (file.Units.TryGetValue(type, out var stats))
            {
                units[type] = Changes($"{at}.units.{type}", stats, changes, errors);
                var unit = Apply(stats, units[type]);
                CheckValues($"{at}.units.{type}", unit, AtLeastOne(unit), errors, units[type].Keys);
            }
        }

        var buildings = new Dictionary<BuildingType, IReadOnlyDictionary<string, int>>();
        foreach (var (type, changes) in upgrade.Buildings)
        {
            if (file.Buildings.TryGetValue(type, out var stats))
            {
                buildings[type] = Changes($"{at}.buildings.{type}", stats, changes, errors);
                CheckValues($"{at}.buildings.{type}", Apply(stats, buildings[type]), ["buildWork", "maxHp"], errors, buildings[type].Keys);
            }
        }

        var effect = upgrade.Effect.Length > 0
            ? upgrade.Effect
            : string.Join("; ", units.Select(u => $"{u.Key}: {Describe(u.Value)}").Concat(buildings.Select(b => $"{b.Key}: {Describe(b.Value)}")));
        return upgrade with { Units = units, Buildings = buildings, Effect = effect };

        static string Describe(IReadOnlyDictionary<string, int> changes) => string.Join(", ", changes.Select(c => $"{c.Key} {c.Value}"));
    }

    /// <summary>An upgrade's changes for one type, keyed by the stat's own spelling; only number stats can change.</summary>
    private static Dictionary<string, int> Changes<T>(string at, T stats, IReadOnlyDictionary<string, int> changes, List<string> errors)
    {
        var fields = Fields(stats);
        var numbers = fields.Where(f => IsNumber(f.Value)).Select(f => f.Key).ToList();
        var result = new Dictionary<string, int>();
        foreach (var (stat, value) in changes)
        {
            if (numbers.FirstOrDefault(n => n.Equals(stat, StringComparison.OrdinalIgnoreCase)) is { } name)
            {
                result[name] = value;
            }
            else
            {
                errors.Add($"{at}.{stat}: upgrades can only set {string.Join(", ", numbers)}.");
            }
        }

        return result;
    }

    /// <summary>The rules file as written.</summary>
    private sealed record RulesFile
    {
        public required EconomyRules Economy { get; init; }
        public required SerumRules Serum { get; init; }
        public required IReadOnlyDictionary<UnitType, UnitStats> Units { get; init; }
        public required IReadOnlyDictionary<BuildingType, BuildingStats> Buildings { get; init; }
        public IReadOnlyDictionary<UpgradeType, UpgradeStats> Upgrades { get; init; } = new Dictionary<UpgradeType, UpgradeStats>();
    }
}
