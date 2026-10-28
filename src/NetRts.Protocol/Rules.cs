namespace NetRts.Protocol;

/// <summary>Static game data so bots can be data driven instead of hard-coding numbers.</summary>
public sealed record RulesDto
{
    public IReadOnlyList<UnitStatsDto> Units { get; init; } = [];
    public IReadOnlyList<BuildingStatsDto> Buildings { get; init; } = [];
    public IReadOnlyList<UpgradeStatsDto> Upgrades { get; init; } = [];
    public required EconomyRulesDto Economy { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public sealed record UnitStatsDto
{
    public UnitType Type { get; init; }
    public int Cost { get; init; }
    public int BuildTicks { get; init; }
    public int MaxHp { get; init; }
    public int Damage { get; init; }
    public int Armor { get; init; }

    /// <summary>Attack reach in tiles (Chebyshev distance: diagonals count as 1).</summary>
    public int Range { get; init; }

    /// <summary>Targets closer than this (Chebyshev) are too close to hit. 0 = no minimum.</summary>
    public int MinRange { get; init; }

    /// <summary>Ticks between attacks.</summary>
    public int AttackCooldown { get; init; }

    /// <summary>
    /// 0 = attacks land at once. Otherwise the unit lobs a shell at the target's tile that lands this many
    /// ticks later, hitting every enemy on that tile then; targets that move off it are missed.
    /// </summary>
    public int ShotTicks { get; init; }

    /// <summary>Sight radius in tiles (Euclidean).</summary>
    public int Vision { get; init; }

    /// <summary>Tenths of a tile per tick (10 = one tile per tick).</summary>
    public int Speed { get; init; }

    /// <summary>The building that trains it; null for a unit nothing trains (one you only start with).</summary>
    public BuildingType? ProducedAt { get; init; }

    /// <summary>Gathers ore and constructs buildings (Gather and Build select only these), and never fights back on its own.</summary>
    public bool IsWorker { get; init; }

    /// <summary>Ore a worker carries per trip.</summary>
    public int CarryCapacity { get; init; }

    /// <summary>Ore a worker mines per tick.</summary>
    public int GatherPerTick { get; init; }
}

public sealed record BuildingStatsDto
{
    public BuildingType Type { get; init; }
    public int Cost { get; init; }

    /// <summary>Worker-ticks of construction (two workers build twice as fast).</summary>
    public int BuildWork { get; init; }

    public int MaxHp { get; init; }
    public int Armor { get; init; }
    public int Vision { get; init; }
    public int Damage { get; init; }
    public int Range { get; init; }
    public int AttackCooldown { get; init; }
    public int StorageBonus { get; init; }
    public bool IsDropOff { get; init; }
    public IReadOnlyList<UnitType> Produces { get; init; } = [];
    public bool CanResearch { get; init; }
    public IReadOnlyList<BuildingType> Requires { get; init; } = [];

    /// <summary>An upgrade that must be researched before this building can be started, if any.</summary>
    public UpgradeType? RequiresUpgrade { get; init; }

    /// <summary>Lose every headquarters building (finished or under construction) and you are eliminated.</summary>
    public bool IsHeadquarters { get; init; }
}

public sealed record UpgradeStatsDto
{
    public UpgradeType Type { get; init; }
    public int Cost { get; init; }
    public int ResearchTicks { get; init; }
    public UpgradeType? Requires { get; init; }
    public string Effect { get; init; } = "";

    /// <summary>
    /// The unit stats this upgrade sets, by unit type and then stat name, e.g. <c>{ "Soldier": { "damage": 11 } }</c>.
    /// They replace the researcher's current values for those units, the ones already on the field included
    /// (HP keeps the same percentage of max HP). If two upgrades set the same stat, the one researched last wins.
    /// </summary>
    public IReadOnlyDictionary<UnitType, IReadOnlyDictionary<string, int>> Units { get; init; } =
        new Dictionary<UnitType, IReadOnlyDictionary<string, int>>();

    /// <summary>The building stats this upgrade sets, in the same way as <see cref="Units"/>.</summary>
    public IReadOnlyDictionary<BuildingType, IReadOnlyDictionary<string, int>> Buildings { get; init; } =
        new Dictionary<BuildingType, IReadOnlyDictionary<string, int>>();
}

public sealed record EconomyRulesDto
{
    public int StartingResources { get; init; }

    /// <summary>How many of the starting units are workers.</summary>
    public int StartingWorkers { get; init; }

    /// <summary>The first worker type's base carry capacity; each unit type has its own (<see cref="UnitStatsDto.CarryCapacity"/>).</summary>
    public int WorkerCarryCapacity { get; init; }

    /// <summary>The first worker type's base mining rate; each unit type has its own (<see cref="UnitStatsDto.GatherPerTick"/>).</summary>
    public int GatherPerTick { get; init; }

    public int MaxProductionQueue { get; init; }

    /// <summary>The building each player starts with (a headquarters).</summary>
    public BuildingType StartingBuilding { get; init; }

    /// <summary>The units each player starts with, by type.</summary>
    public IReadOnlyDictionary<UnitType, int> StartingUnits { get; init; } = new Dictionary<UnitType, int>();
}
