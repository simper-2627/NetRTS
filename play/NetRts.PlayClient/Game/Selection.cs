using NetRts.Protocol;

namespace NetRts.PlayClient.Game;

/// <summary>Where a click landed: the tile, and the unit drawn under the pointer, if any.</summary>
public sealed record Pick(int X, int Y, int? UnitId = null);

/// <summary>
/// What the player has selected: some of their own units, or one of their buildings, or one enemy
/// thing to look at (which takes no orders). Rules follow the usual RTS conventions.
/// </summary>
public sealed class Selection
{
    private readonly List<int> _units = [];

    /// <summary>Selected own units, in id order.</summary>
    public IReadOnlyList<int> Units => _units;

    /// <summary>A selected own building (only when no units are selected).</summary>
    public int? Building { get; private set; }

    /// <summary>An enemy unit or building, or a deposit, being inspected.</summary>
    public int? Inspected { get; private set; }

    public bool IsEmpty => _units.Count == 0 && Building is null && Inspected is null;

    public event Action? Changed;

    /// <summary>
    /// A left click: your unit under the pointer (shift adds or removes it), else your building on the
    /// tile, else anything else there to inspect. A click on empty ground clears the selection unless
    /// shift is held.
    /// </summary>
    public void Click(GameStateDto state, Pick pick, bool shift)
    {
        var me = state.You.Slot;
        var unit = pick.UnitId is { } id ? state.Units.FirstOrDefault(u => u.Id == id) : null;
        unit ??= state.Units.Where(u => u.X == pick.X && u.Y == pick.Y).OrderByDescending(u => u.Owner == me).ThenBy(u => u.Id).FirstOrDefault();
        if (unit is not null && unit.Owner == me)
        {
            if (shift && Building is null)
            {
                if (!_units.Remove(unit.Id))
                {
                    _units.Add(unit.Id);
                    _units.Sort();
                }
            }
            else
            {
                Replace([unit.Id]);
            }

            Inspected = null;
            Building = null;
            Changed?.Invoke();
            return;
        }

        if (shift)
        {
            return; // shift-clicking something you can't add to the group leaves the group alone
        }

        var building = state.Buildings.FirstOrDefault(b => b.X == pick.X && b.Y == pick.Y);
        if (building is not null && building.Owner == me)
        {
            SetBuilding(building.Id);
            return;
        }

        var other = unit?.Id ?? building?.Id
            ?? state.RememberedBuildings.FirstOrDefault(b => b.X == pick.X && b.Y == pick.Y)?.Id
            ?? state.Resources.FirstOrDefault(r => r.X == pick.X && r.Y == pick.Y)?.Id;
        _units.Clear();
        Building = null;
        Inspected = other;
        Changed?.Invoke();
    }

    /// <summary>A drag box: the player's units drawn inside it (shift adds them to the selection).</summary>
    public void Box(GameStateDto state, IEnumerable<int> unitIds, bool shift)
    {
        var mine = OwnUnits(state, unitIds);
        if (mine.Count == 0)
        {
            if (!shift)
            {
                Clear();
            }

            return;
        }

        if (shift && Building is null)
        {
            mine.AddRange(_units);
        }

        Replace(mine);
        Building = null;
        Inspected = null;
        Changed?.Invoke();
    }

    /// <summary>Double-click (or ctrl+click) a unit of yours: every unit of yours of the same type.</summary>
    public void SelectType(GameStateDto state, Pick pick, bool shift)
    {
        var me = state.You.Slot;
        var unit = pick.UnitId is { } id ? state.Units.FirstOrDefault(u => u.Id == id && u.Owner == me) : null;
        unit ??= state.Units.FirstOrDefault(u => u.X == pick.X && u.Y == pick.Y && u.Owner == me);
        if (unit is null)
        {
            Click(state, pick, shift);
            return;
        }

        var all = state.Units.Where(u => u.Owner == me && u.Type == unit.Type).Select(u => u.Id).ToList();
        if (shift && Building is null)
        {
            all.AddRange(_units);
        }

        Replace(all);
        Building = null;
        Inspected = null;
        Changed?.Invoke();
    }

    /// <summary>Selects exactly these units (a control group), keeping only the ones still alive.</summary>
    public void SetUnits(GameStateDto state, IEnumerable<int> unitIds)
    {
        Replace(OwnUnits(state, unitIds));
        Building = null;
        Inspected = null;
        Changed?.Invoke();
    }

    public void SetBuilding(int buildingId)
    {
        _units.Clear();
        Building = buildingId;
        Inspected = null;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (IsEmpty)
        {
            return;
        }

        _units.Clear();
        Building = null;
        Inspected = null;
        Changed?.Invoke();
    }

    /// <summary>After each tick: drop what died, or what went back into the fog.</summary>
    public void Prune(GameStateDto state)
    {
        var alive = state.Units.Where(u => u.Owner == state.You.Slot).Select(u => u.Id).ToHashSet();
        var changed = _units.RemoveAll(id => !alive.Contains(id)) > 0;
        if (Building is { } b && !state.Buildings.Any(x => x.Id == b && x.Owner == state.You.Slot))
        {
            Building = null;
            changed = true;
        }

        if (Inspected is { } i && !state.Units.Any(u => u.Id == i) && !state.Buildings.Any(x => x.Id == i)
            && !state.RememberedBuildings.Any(x => x.Id == i) && !state.Resources.Any(r => r.Id == i))
        {
            Inspected = null;
            changed = true;
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private void Replace(IEnumerable<int> ids)
    {
        _units.Clear();
        _units.AddRange(ids.Distinct().Order());
    }

    private static List<int> OwnUnits(GameStateDto state, IEnumerable<int> ids)
    {
        var wanted = ids.ToHashSet();
        return state.Units.Where(u => u.Owner == state.You.Slot && wanted.Contains(u.Id)).Select(u => u.Id).ToList();
    }
}
