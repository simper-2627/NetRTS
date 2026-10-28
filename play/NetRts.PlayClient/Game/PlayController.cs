using Microsoft.JSInterop;
using NetRts.Protocol;

namespace NetRts.PlayClient.Game;

/// <summary>
/// The player's hands: turns pointer and key input from <c>playview.js</c> into selection changes
/// and orders, and hands the orders to the <see cref="GameSession"/> queue.
/// </summary>
public sealed class PlayController
{
    private readonly GameSession _session;
    private readonly Dictionary<int, ControlGroup> _groups = [];
    private PlayView? _view;
    private int _spent;
    private int _spentTick = -1;
    private int _noticeTick;
    private (int Group, DateTime At)? _lastRecall;
    private int? _lastIdleWorker;

    public PlayController(GameSession session) => _session = session;

    public Selection Selection { get; } = new();

    public DepositMemory Deposits { get; } = new();

    /// <summary>"attack" or "build" while the next left click on the map picks a target; null otherwise.</summary>
    public string? Mode { get; private set; }

    /// <summary>The building being placed while <see cref="Mode"/> is "build".</summary>
    public BuildingType? Placing { get; private set; }

    /// <summary>Why the build ghost is red where the pointer is, if it is.</summary>
    public string? PlacementProblem { get; private set; }

    /// <summary>A short message for the player: an order refused here before it went to the server.</summary>
    public string? Notice { get; private set; }

    public Lore Lore => _view?.Lore ?? Lore.Classic;

    /// <summary>Ore left after the orders already given this tick: the state only updates next tick.</summary>
    public int Budget => (_session.State?.You.Resources ?? 0) - (_session.State?.Tick == _spentTick ? _spent : 0);

    public IReadOnlyDictionary<int, ControlGroup> Groups => _groups;

    /// <summary>What P does: pause or resume, where the match allows it. Set by the page.</summary>
    public Func<Task>? PauseToggle { get; init; }

    /// <summary>Raised when the selection, the mode or a notice changed, for the HUD.</summary>
    public event Action? Changed;

    public void Attach(PlayView view) => _view = view;

    /// <summary>Called after every new state, before the HUD redraws.</summary>
    public async Task OnStateAsync(GameStateDto state)
    {
        Deposits.Update(state);
        var before = (Selection.Units.Count, Selection.Building, Selection.Inspected);
        Selection.Prune(state);
        if (before != (Selection.Units.Count, Selection.Building, Selection.Inspected))
        {
            await PushSelectionAsync();
        }

        if (Notice is not null && state.Tick - _noticeTick >= 3)
        {
            Notice = null;
        }

        if (Mode == "build" && !Selection.Units.Any(id => state.Units.Any(u => u.Id == id && u.Type == UnitType.Worker)))
        {
            await SetModeAsync(null); // the builders died
        }
        else if (Mode == "build")
        {
            await PushGhostAsync();
        }
    }

    // ---------- pointer input from playview.js

    [JSInvokable]
    public async Task OnClick(int x, int y, int? unitId, bool shift, bool ctrl)
    {
        if (_session.State is not { } state)
        {
            return;
        }

        if (Mode == "attack")
        {
            await SetModeAsync(null);
            await IssueAsync(OrderBuilder.AttackMove(state, Selection, x, y, Lore), x, y, "attack");
            return;
        }

        if (Mode == "build" && Placing is { } type)
        {
            await PlaceAsync(state, type, x, y, keepPlacing: shift);
            return;
        }

        if (ctrl)
        {
            Selection.SelectType(state, new Pick(x, y, unitId), shift);
        }
        else
        {
            Selection.Click(state, new Pick(x, y, unitId), shift);
        }

        await PushSelectionAsync();
    }

    [JSInvokable]
    public async Task OnDoubleClick(int x, int y, int? unitId, bool shift)
    {
        if (_session.State is { } state)
        {
            Selection.SelectType(state, new Pick(x, y, unitId), shift);
            await PushSelectionAsync();
        }
    }

    [JSInvokable]
    public async Task OnBox(int[] unitIds, bool shift)
    {
        if (_session.State is { } state)
        {
            Selection.Box(state, unitIds, shift);
            await PushSelectionAsync();
        }
    }

    [JSInvokable]
    public async Task OnRightClick(int x, int y, int? unitId, bool shift)
    {
        if (Mode is not null)
        {
            await SetModeAsync(null); // right-click cancels a targeting mode, as in most RTS games
            return;
        }

        if (_session.State is not { } state)
        {
            return;
        }

        var orders = OrderBuilder.Smart(state, Deposits, Selection, new Pick(x, y, unitId), Lore);
        var kind = orders.FirstOrDefault()?.Command switch
        {
            { Type: CommandType.Attack } => "attack",
            { Type: CommandType.Rally } => "rally",
            _ => "move",
        };
        await IssueAsync(orders, x, y, kind);
    }

    /// <summary>The pointer moved to another tile while placing a building.</summary>
    [JSInvokable]
    public void OnHover(int x, int y)
    {
        if (Mode != "build" || Placing is not { } type || _session.State is not { } state || _session.Map is not { } map || _session.Rules is not { } rules)
        {
            return;
        }

        var problem = Placement.Problem(state, map, Deposits, rules, type, x, y, Budget, Lore);
        if (problem != PlacementProblem)
        {
            PlacementProblem = problem;
            Changed?.Invoke();
        }
    }

    [JSInvokable]
    public async Task OnKey(string key, bool ctrl, bool shift)
    {
        if (_session.State is not { } state)
        {
            return;
        }

        if (key.Length == 1 && char.IsAsciiDigit(key[0]))
        {
            await ControlGroupAsync(state, key[0] - '0', ctrl, shift);
            return;
        }

        if (key == "Escape")
        {
            if (Mode is not null)
            {
                await SetModeAsync(null);
            }
            else
            {
                Selection.Clear();
                await PushSelectionAsync();
            }

            return;
        }

        if (key == " ")
        {
            await LookAtSelectionAsync(state);
            return;
        }

        if (key.Length != 1 || ctrl)
        {
            return;
        }

        var c = char.ToUpperInvariant(key[0]);
        if (c == 'I')
        {
            await JumpToIdleWorkerAsync();
            return;
        }

        if (c == 'P')
        {
            if (PauseToggle is { } toggle)
            {
                await toggle();
            }

            return;
        }

        if (Selection.Units.Count > 0)
        {
            if (c == 'A')
            {
                await AttackMoveModeAsync();
                return;
            }

            if (c == 'S')
            {
                await StopAsync();
                return;
            }
        }

        if (CommandCard.ForKey(Buttons(), c) is { } button)
        {
            await PressAsync(button, shift);
        }
    }

    // ---------- the command card

    /// <summary>The buttons for the current selection: building types for workers, training and research for a building.</summary>
    public IReadOnlyList<CardButton> Buttons()
    {
        if (_session.State is not { } state || _session.Rules is not { } rules)
        {
            return [];
        }

        if (Selection.Units.Count > 0)
        {
            var workers = state.Units.Any(u => u.Type == UnitType.Worker && Selection.Units.Contains(u.Id));
            return workers ? CommandCard.BuildButtons(state, rules, Budget, Lore) : [];
        }

        return Selection.Building is { } id && state.Buildings.FirstOrDefault(b => b.Id == id) is { } building
            ? CommandCard.BuildingButtons(state, rules, building, Budget, Lore)
            : [];
    }

    /// <summary>A command-card button (or its hotkey). Shift trains five instead of one, or keeps placing buildings.</summary>
    public async Task PressAsync(CardButton button, bool shift = false)
    {
        if (_session.State is not { } state || _session.Rules is not { } rules)
        {
            return;
        }

        if (button.Blocked is { } why)
        {
            await NoticeAsync(state, why);
            return;
        }

        switch (button.Action)
        {
            case BuildAction build:
                Placing = build.Type;
                PlacementProblem = null;
                await SetModeAsync("build");
                break;

            case ProduceAction produce when Selection.Building is { } buildingId:
                var room = rules.Economy.MaxProductionQueue - (state.Buildings.FirstOrDefault(b => b.Id == buildingId)?.Production.Count ?? 0);
                var count = Math.Max(1, Math.Min(shift ? 5 : 1, Math.Min(room, button.Cost == 0 ? room : Budget / button.Cost)));
                Spend(state, button.Cost * count);
                await IssueAsync(
                    [new Order(new CommandRequest { Type = CommandType.Produce, BuildingId = buildingId, UnitType = produce.Type, Count = count },
                        $"Train {(count == 1 ? Lore.Unit(produce.Type) : $"{count} {Lore.Unit(produce.Type)}s")}")],
                    null, null, null);
                break;

            case ResearchAction research when Selection.Building is { } labId:
                Spend(state, button.Cost);
                await IssueAsync(
                    [new Order(new CommandRequest { Type = CommandType.Research, BuildingId = labId, Upgrade = research.Upgrade }, $"Research {Lore.Upgrade(research.Upgrade)}")],
                    null, null, null);
                break;
        }
    }

    public async Task AttackMoveModeAsync() => await SetModeAsync(Selection.Units.Count > 0 ? "attack" : null);

    public async Task StopAsync()
    {
        if (_session.State is { } state)
        {
            await IssueAsync(OrderBuilder.Stop(state, Selection, Lore), null, null, null);
        }
    }

    public async Task CancelModeAsync() => await SetModeAsync(null);

    /// <summary>Your workers with nothing to do, in id order.</summary>
    public IReadOnlyList<UnitDto> IdleWorkers() =>
        _session.State is { } state
            ? state.Units.Where(u => u.Owner == state.You.Slot && u.Type == UnitType.Worker && u.Activity == UnitActivity.Idle).OrderBy(u => u.Id).ToList()
            : [];

    /// <summary>Selects the next idle worker (after the last one jumped to, wrapping round) and looks at it.</summary>
    public async Task JumpToIdleWorkerAsync()
    {
        var idle = IdleWorkers();
        if (idle.Count == 0 || _session.State is not { } state)
        {
            return;
        }

        var next = idle.FirstOrDefault(u => u.Id > (_lastIdleWorker ?? int.MinValue)) ?? idle[0];
        _lastIdleWorker = next.Id;
        Selection.SetUnits(state, [next.Id]);
        await PushSelectionAsync();
        await LookAtAsync(next.X, next.Y);
    }

    /// <summary>Moves the camera, e.g. to the player's HQ at the start.</summary>
    public async Task LookAtAsync(int x, int y)
    {
        if (_view is not null)
        {
            await _view.LookAtAsync(x, y);
        }
    }

    // ---------- control groups: Ctrl+n sets, Shift+n adds, n selects, n twice looks at it

    private async Task ControlGroupAsync(GameStateDto state, int n, bool ctrl, bool shift)
    {
        if (ctrl || shift)
        {
            var group = new ControlGroup([.. Selection.Units], Selection.Units.Count == 0 ? Selection.Building : null);
            if (shift && _groups.TryGetValue(n, out var old) && group.Building is null)
            {
                group = new ControlGroup([.. old.Units.Union(group.Units).Order()], null);
            }

            if (group.Units.Length == 0 && group.Building is null)
            {
                _groups.Remove(n);
            }
            else
            {
                _groups[n] = group;
            }

            Changed?.Invoke();
            return;
        }

        if (!_groups.TryGetValue(n, out var recalled))
        {
            return;
        }

        if (recalled.Building is { } building && state.Buildings.Any(b => b.Id == building && b.Owner == state.You.Slot))
        {
            Selection.SetBuilding(building);
        }
        else
        {
            Selection.SetUnits(state, recalled.Units);
        }

        await PushSelectionAsync();
        var now = DateTime.UtcNow;
        if (_lastRecall is { } last && last.Group == n && now - last.At < TimeSpan.FromMilliseconds(450))
        {
            await LookAtSelectionAsync(state);
        }

        _lastRecall = (n, now);
    }

    private async Task LookAtSelectionAsync(GameStateDto state)
    {
        var me = state.You.Slot;
        var units = state.Units.Where(u => Selection.Units.Contains(u.Id)).ToList();
        if (units.Count > 0)
        {
            await LookAtAsync((int)Math.Round(units.Average(u => u.X)), (int)Math.Round(units.Average(u => u.Y)));
        }
        else
        {
            var building = (Selection.Building is { } id ? state.Buildings.FirstOrDefault(b => b.Id == id) : null)
                ?? state.Buildings.Where(b => b.Owner == me && b.Type == BuildingType.CommandCenter).OrderBy(b => b.Id).FirstOrDefault();
            if (building is not null)
            {
                await LookAtAsync(building.X, building.Y);
            }
        }
    }

    // ---------- internals

    private async Task PlaceAsync(GameStateDto state, BuildingType type, int x, int y, bool keepPlacing)
    {
        if (_session.Map is not { } map || _session.Rules is not { } rules)
        {
            return;
        }

        if (Placement.Problem(state, map, Deposits, rules, type, x, y, Budget, Lore) is { } problem)
        {
            await NoticeAsync(state, problem);
            return;
        }

        var workers = state.Units.Where(u => u.Type == UnitType.Worker && Selection.Units.Contains(u.Id)).ToList();
        if (workers.Count == 0)
        {
            await SetModeAsync(null);
            return;
        }

        var helping = state.Buildings.Any(b => b.X == x && b.Y == y);
        if (!helping)
        {
            Spend(state, CommandCard.BuildingCost(state, rules, type));
        }

        if (!keepPlacing)
        {
            await SetModeAsync(null);
        }
        else
        {
            await PushGhostAsync();
        }

        await IssueAsync(
            [new Order(new CommandRequest { Type = CommandType.Build, UnitIds = [.. workers.Select(u => u.Id)], BuildingType = type, X = x, Y = y },
                $"{OrderBuilder.Describe(workers, Lore)}: {(helping ? "help build" : "build")} {Lore.Building(type, state.You.Slot)} at ({x}, {y})")],
            x, y, "move");
    }

    private void Spend(GameStateDto state, int amount)
    {
        if (_spentTick != state.Tick)
        {
            _spentTick = state.Tick;
            _spent = 0;
        }

        _spent += amount;
    }

    private async Task NoticeAsync(GameStateDto state, string message)
    {
        Notice = message;
        _noticeTick = state.Tick;
        Changed?.Invoke();
        await Task.CompletedTask;
    }

    private async Task IssueAsync(IReadOnlyList<Order> orders, int? x, int? y, string? flash)
    {
        if (orders.Count == 0)
        {
            return;
        }

        Notice = null;
        if (_view is not null && x is { } fx && y is { } fy && flash is not null)
        {
            await _view.FlashAsync(fx, fy, flash);
        }

        await _session.OrderAsync(orders);
        Changed?.Invoke();
    }

    private async Task SetModeAsync(string? mode)
    {
        Mode = mode;
        if (mode != "build")
        {
            Placing = null;
            PlacementProblem = null;
        }

        if (_view is not null)
        {
            await _view.SetModeAsync(mode);
            if (mode == "build")
            {
                await PushGhostAsync();
            }
        }

        Changed?.Invoke();
    }

    private async Task PushGhostAsync()
    {
        if (_view is null || Placing is not { } type || _session.State is not { } state || _session.Map is not { } map || _session.Rules is not { } rules)
        {
            return;
        }

        await _view.SetGhostAsync(type, state.You.Slot, Placement.Mask(state, map, Deposits, rules, type, Budget));
    }

    private async Task PushSelectionAsync()
    {
        if (Mode == "build" && Selection.Units.Count == 0)
        {
            await SetModeAsync(null);
        }

        if (_view is not null)
        {
            await _view.SetSelectionAsync(Selection);
        }

        Changed?.Invoke();
    }
}

/// <summary>A saved selection: units, or one building.</summary>
public sealed record ControlGroup(int[] Units, int? Building);
