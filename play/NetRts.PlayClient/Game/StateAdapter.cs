using NetRts.Protocol;

namespace NetRts.PlayClient.Game;

/// <summary>
/// What <c>MapView.setState</c> draws: the shape of a spectator frame (units, buildings, resources and
/// per-player visibility), built from one player's fogged <see cref="GameStateDto"/>. Serialized
/// camelCase by the JS interop, like the server's JSON.
/// </summary>
public sealed record RenderState
{
    public int Tick { get; init; }
    public int TickIntervalMs { get; init; }
    public MatchStatus Status { get; init; }
    public IReadOnlyList<UnitDto> Units { get; init; } = [];
    public IReadOnlyList<RenderBuilding> Buildings { get; init; } = [];
    public IReadOnlyList<ResourceDepositDto> Resources { get; init; } = [];

    /// <summary>Only the player whose view this is: <c>MapView</c> draws fog from its visibility rows.</summary>
    public IReadOnlyList<RenderPlayer> Players { get; init; } = [];
}

/// <summary>A building as <c>MapView</c> draws it; <see cref="Remembered"/> ones are last-seen enemy buildings under the fog.</summary>
public sealed record RenderBuilding
{
    public int Id { get; init; }
    public int Owner { get; init; }
    public BuildingType Type { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Hp { get; init; }
    public int MaxHp { get; init; }
    public bool Completed { get; init; }
    public int ConstructionPercent { get; init; }
    public IReadOnlyList<ProductionDto> Production { get; init; } = [];
    public ResearchDto? Research { get; init; }
    public PositionDto? Rally { get; init; }
    public bool Remembered { get; init; }
    public int? LastSeenTick { get; init; }
}

public sealed record RenderPlayer(int Slot, string Name, IReadOnlyList<string> Visibility);

public static class StateAdapter
{
    /// <summary>
    /// Everything in the fogged state, plus remembered enemy buildings drawn where they were last
    /// seen. Those tiles are fogged, so the fog layer dims them.
    /// </summary>
    public static RenderState ToRenderState(GameStateDto state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var visible = state.Buildings.Select(b => new RenderBuilding
        {
            Id = b.Id,
            Owner = b.Owner,
            Type = b.Type,
            X = b.X,
            Y = b.Y,
            Hp = b.Hp,
            MaxHp = b.MaxHp,
            Completed = b.Completed,
            ConstructionPercent = b.ConstructionPercent,
            Production = b.Production,
            Research = b.Research,
            Rally = b.Rally,
        }).ToList();
        var seen = visible.Select(b => b.Id).ToHashSet();
        var remembered = state.RememberedBuildings.Where(b => !seen.Contains(b.Id)).Select(b => new RenderBuilding
        {
            Id = b.Id,
            Owner = b.Owner,
            Type = b.Type,
            X = b.X,
            Y = b.Y,
            Hp = b.Hp,
            MaxHp = b.MaxHp,
            Completed = b.Completed,
            ConstructionPercent = b.Completed ? 100 : 0,
            Remembered = true,
            LastSeenTick = b.LastSeenTick,
        });

        return new RenderState
        {
            Tick = state.Tick,
            TickIntervalMs = state.TickIntervalMs,
            Status = state.Status,
            Units = state.Units,
            Buildings = [.. visible, .. remembered],
            Resources = state.Resources,
            Players = [new RenderPlayer(state.You.Slot, state.You.Name, state.Visibility)],
        };
    }
}
