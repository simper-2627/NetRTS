using NetRts.Protocol;

namespace NetRts.PlayClient.Game;

/// <summary>An API command plus how the HUD describes it.</summary>
public sealed record Order(CommandRequest Command, string Label);

/// <summary>
/// Deposits the player has seen. They never move, and the API lets workers be sent to any deposit
/// you have seen, even in the fog, so the client remembers them until they are seen gone.
/// </summary>
public sealed class DepositMemory
{
    private readonly Dictionary<int, ResourceDepositDto> _known = [];

    public IReadOnlyCollection<ResourceDepositDto> Known => _known.Values;

    public void Update(GameStateDto state)
    {
        foreach (var r in state.Resources)
        {
            _known[r.Id] = r;
        }

        var visible = state.Resources.Select(r => r.Id).ToHashSet();
        foreach (var r in _known.Values.ToList())
        {
            var row = r.Y < state.Visibility.Count ? state.Visibility[r.Y] : null;
            var inSight = row is not null && r.X < row.Length && row[r.X] == '1';
            if ((inSight && !visible.Contains(r.Id)) || r.Remaining <= 0)
            {
                _known.Remove(r.Id);
            }
        }

        foreach (var e in state.Events)
        {
            if (e.Kind == "DepositDepleted" && e.EntityId is { } id)
            {
                _known.Remove(id);
            }
        }
    }

    public ResourceDepositDto? At(int x, int y) => _known.Values.FirstOrDefault(r => r.X == x && r.Y == y);
}

/// <summary>Turns the player's clicks into the same commands a bot would send.</summary>
public static class OrderBuilder
{
    /// <summary>
    /// A right-click on (x, y). With units selected: attack an enemy that is there (or attack-move to an
    /// enemy building last seen there), send workers to mine a deposit, send workers to help finish
    /// your own construction site, otherwise move. Units that can't do the main thing (soldiers on a
    /// deposit) just move there. With one of your buildings selected: set its rally point.
    /// </summary>
    public static IReadOnlyList<Order> Smart(GameStateDto state, DepositMemory deposits, Selection selection, Pick pick, Lore lore)
    {
        var me = state.You.Slot;
        var (x, y) = (pick.X, pick.Y);
        if (selection.Units.Count == 0)
        {
            return selection.Building is { } rallying
                ? [new Order(new CommandRequest { Type = CommandType.Rally, BuildingId = rallying, X = x, Y = y }, $"Rally point ({x}, {y})")]
                : [];
        }

        var units = state.Units.Where(u => u.Owner == me && selection.Units.Contains(u.Id)).ToList();
        if (units.Count == 0)
        {
            return [];
        }

        var all = units.Select(u => u.Id).ToArray();
        var workerUnits = units.Where(u => u.Type == UnitType.Worker).ToList();
        var otherUnits = units.Where(u => u.Type != UnitType.Worker).ToList();
        var workers = workerUnits.Select(u => u.Id).ToArray();
        var others = otherUnits.Select(u => u.Id).ToArray();
        var who = Describe(units, lore);

        var enemyUnit = (pick.UnitId is { } picked ? state.Units.FirstOrDefault(u => u.Id == picked && u.Owner != me) : null)
            ?? state.Units.Where(u => u.Owner != me && u.X == x && u.Y == y).OrderBy(u => u.Id).FirstOrDefault();
        if (enemyUnit is not null)
        {
            return [Attack(all, enemyUnit.Id, $"{who}: attack {lore.Unit(enemyUnit.Type)} #{enemyUnit.Id}")];
        }

        var building = state.Buildings.FirstOrDefault(b => b.X == x && b.Y == y);
        if (building is not null && building.Owner != me)
        {
            return [Attack(all, building.Id, $"{who}: attack {lore.Building(building.Type, building.Owner)} #{building.Id}")];
        }

        if (state.RememberedBuildings.FirstOrDefault(b => b.X == x && b.Y == y && b.Owner != me) is { } remembered)
        {
            return [AttackMove(all, x, y, $"{who}: attack-move to the {lore.Building(remembered.Type, remembered.Owner)} last seen at ({x}, {y})")];
        }

        var orders = new List<Order>();
        if (workers.Length > 0 && deposits.At(x, y) is { } deposit)
        {
            orders.Add(new Order(new CommandRequest { Type = CommandType.Gather, UnitIds = workers, TargetId = deposit.Id },
                $"{Describe(workerUnits, lore)}: mine {lore.Ore} #{deposit.Id}"));
            if (others.Length > 0)
            {
                orders.Add(Move(others, x, y, $"{Describe(otherUnits, lore)}: move to ({x}, {y})"));
            }

            return orders;
        }

        if (workers.Length > 0 && building is { Completed: false } site && site.Owner == me)
        {
            orders.Add(new Order(new CommandRequest { Type = CommandType.Build, UnitIds = workers, BuildingType = site.Type, X = x, Y = y },
                $"{Describe(workerUnits, lore)}: help build {lore.Building(site.Type, site.Owner)} #{site.Id}"));
            if (others.Length > 0)
            {
                orders.Add(Move(others, x, y, $"{Describe(otherUnits, lore)}: move to ({x}, {y})"));
            }

            return orders;
        }

        return [Move(all, x, y, $"{who}: move to ({x}, {y})")];
    }

    /// <summary>Walk to (x, y), fighting anything seen on the way.</summary>
    public static IReadOnlyList<Order> AttackMove(GameStateDto state, Selection selection, int x, int y, Lore lore)
    {
        var units = OwnSelected(state, selection);
        return units.Count == 0 ? [] : [AttackMove(units.Select(u => u.Id).ToArray(), x, y, $"{Describe(units, lore)}: attack-move to ({x}, {y})")];
    }

    /// <summary>Drop every order and go idle.</summary>
    public static IReadOnlyList<Order> Stop(GameStateDto state, Selection selection, Lore lore)
    {
        var units = OwnSelected(state, selection);
        return units.Count == 0
            ? []
            : [new Order(new CommandRequest { Type = CommandType.Stop, UnitIds = units.Select(u => u.Id).ToArray() }, $"{Describe(units, lore)}: stop")];
    }

    /// <summary>"Worker #31", "3 Soldiers", or "5 units".</summary>
    public static string Describe(IReadOnlyList<UnitDto> units, Lore lore)
    {
        if (units.Count == 1)
        {
            return $"{lore.Unit(units[0].Type)} #{units[0].Id}";
        }

        var types = units.Select(u => u.Type).Distinct().ToList();
        return types.Count == 1 ? $"{units.Count} {lore.Unit(types[0])}s" : $"{units.Count} units";
    }

    private static List<UnitDto> OwnSelected(GameStateDto state, Selection selection) =>
        state.Units.Where(u => u.Owner == state.You.Slot && selection.Units.Contains(u.Id)).ToList();

    private static Order Attack(int[] units, int target, string label) =>
        new(new CommandRequest { Type = CommandType.Attack, UnitIds = units, TargetId = target }, label);

    private static Order AttackMove(int[] units, int x, int y, string label) =>
        new(new CommandRequest { Type = CommandType.Attack, UnitIds = units, X = x, Y = y }, label);

    private static Order Move(int[] units, int x, int y, string label) =>
        new(new CommandRequest { Type = CommandType.Move, UnitIds = units, X = x, Y = y }, label);
}
