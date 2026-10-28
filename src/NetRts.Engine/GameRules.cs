using System.Text.Json.Serialization;
using NetRts.Protocol;

namespace NetRts.Engine;

/// <summary>One unit type's stats, as written in the rules file (camelCase there).</summary>
public sealed record UnitStats
{
    public required int Cost { get; init; }
    public required int BuildTicks { get; init; }
    public required int MaxHp { get; init; }
    public required int Damage { get; init; }
    public required int Armor { get; init; }
    public required int Range { get; init; }
    public required int AttackCooldown { get; init; }
    public required int Vision { get; init; }
    public required int Speed { get; init; }

    /// <summary>The building that trains it; null if nothing does.</summary>
    public BuildingType? ProducedAt { get; init; }

    public int MinRange { get; init; }
    public int ShotTicks { get; init; }

    /// <summary>Gathers and builds, and doesn't fight back on its own.</summary>
    public bool IsWorker { get; init; }

    public int CarryCapacity { get; init; }
    public int GatherPerTick { get; init; }

    /// <summary>Fights and moves faster under the super-soldier serum.</summary>
    public bool SerumBoost { get; init; }
}

/// <summary>One building type's stats, as written in the rules file (camelCase there).</summary>
public sealed record BuildingStats
{
    public required int Cost { get; init; }
    public required int BuildWork { get; init; }
    public required int MaxHp { get; init; }
    public required int Armor { get; init; }
    public required int Vision { get; init; }
    public int Damage { get; init; }
    public int Range { get; init; }
    public int AttackCooldown { get; init; }
    public int StorageBonus { get; init; }
    public bool IsDropOff { get; init; }
    public bool CanResearch { get; init; }
    public IReadOnlyList<BuildingType> Requires { get; init; } = [];
    public UpgradeType? RequiresUpgrade { get; init; }

    /// <summary>Lose every one of these and you're eliminated.</summary>
    public bool IsHeadquarters { get; init; }

    /// <summary>The units whose <see cref="UnitStats.ProducedAt"/> is this building, in rules order.</summary>
    [JsonIgnore]
    public IReadOnlyList<UnitType> Produces { get; init; } = [];
}

/// <summary>
/// One upgrade. Researching it sets the listed stats (unit or building type, then stat name) for the
/// researcher's units and buildings of those types, existing and future.
/// </summary>
public sealed record UpgradeStats
{
    public required int Cost { get; init; }
    public required int ResearchTicks { get; init; }
    public UpgradeType? Requires { get; init; }
    public string Effect { get; init; } = "";
    public IReadOnlyDictionary<UnitType, IReadOnlyDictionary<string, int>> Units { get; init; } = new Dictionary<UnitType, IReadOnlyDictionary<string, int>>();
    public IReadOnlyDictionary<BuildingType, IReadOnlyDictionary<string, int>> Buildings { get; init; } = new Dictionary<BuildingType, IReadOnlyDictionary<string, int>>();
}

public sealed record EconomyRules
{
    public required int StartingResources { get; init; }
    public required BuildingType StartingBuilding { get; init; }
    public required IReadOnlyDictionary<UnitType, int> StartingUnits { get; init; }
    public required int MaxProductionQueue { get; init; }
    public required int UnderConstructionVision { get; init; }
}

/// <summary>The super-soldier serum: units with <see cref="UnitStats.SerumBoost"/> fight and move at <see cref="Factor"/>x for <see cref="Ticks"/> ticks.</summary>
public sealed record SerumRules
{
    public required int Ticks { get; init; }
    public required int Factor { get; init; }
}

/// <summary>
/// The rules the server plays by, read once from <see cref="RuleSet.FileName"/> next to the app (copied
/// there from <c>src/NetRts.Engine</c> on build), and published to bots via GET /api/v1/rules.
/// </summary>
public static class GameRules
{
    /// <summary>Movement points needed to step one tile; unit speed is in the same unit.</summary>
    public const int MovePointsPerTile = 10;

    public static RuleSet Current { get; } = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, RuleSet.FileName));

    public static IReadOnlyDictionary<UnitType, UnitStats> Units => Current.Units;
    public static IReadOnlyDictionary<BuildingType, BuildingStats> Buildings => Current.Buildings;
    public static IReadOnlyDictionary<UpgradeType, UpgradeStats> Upgrades => Current.Upgrades;
    public static int StartingResources => Current.Economy.StartingResources;
    public static int StartingWorkers => Current.StartingWorkers;
    public static int MaxProductionQueue => Current.Economy.MaxProductionQueue;

    internal static int SerumTicks => Current.Serum.Ticks;

    /// <inheritdoc cref="RuleSet.Version"/>
    public static string Version => Current.Version;

    public static RulesDto ToDto() => Current.ToDto();
}
