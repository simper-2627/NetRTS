using NetRts.PlayClient.Game;
using NetRts.Protocol;

namespace NetRts.PlayClient.Tests;

public class SelectionAndOrderTests
{
    // Slot 1 is us. Map (4x2):   x=0 remembered enemy Barracks, x=1 our site, x=2 our TechLab, x=3 deposit
    //                            our workers 31, 32 and soldier 33; enemy soldier 50.
    private static GameStateDto State() => StateAdapterTests.SampleState() with
    {
        Visibility = ["0111", "0111"],
        Units =
        [
            new UnitDto { Id = 31, Owner = 1, Type = UnitType.Worker, X = 2, Y = 1, Hp = 40, MaxHp = 40 },
            new UnitDto { Id = 32, Owner = 1, Type = UnitType.Worker, X = 2, Y = 1, Hp = 40, MaxHp = 40 },
            new UnitDto { Id = 33, Owner = 1, Type = UnitType.Soldier, X = 1, Y = 1, Hp = 110, MaxHp = 110 },
            new UnitDto { Id = 50, Owner = 0, Type = UnitType.Soldier, X = 3, Y = 1, Hp = 90, MaxHp = 110 },
        ],
        Buildings =
        [
            new BuildingDto { Id = 60, Owner = 1, Type = BuildingType.Barracks, X = 1, Y = 0, Hp = 70, MaxHp = 700, Completed = false, ConstructionPercent = 10 },
            new BuildingDto { Id = 77, Owner = 1, Type = BuildingType.TechLab, X = 2, Y = 0, Hp = 550, MaxHp = 550, Completed = true, ConstructionPercent = 100 },
        ],
        Resources = [new ResourceDepositDto { Id = 4, X = 3, Y = 0, Remaining = 940 }],
        RememberedBuildings = [new RememberedBuildingDto { Id = 212, Owner = 0, Type = BuildingType.Barracks, X = 0, Y = 0, Completed = true, LastSeenTick = 31 }],
    };

    private static (Selection Selection, DepositMemory Deposits) Fresh(GameStateDto state)
    {
        var deposits = new DepositMemory();
        deposits.Update(state);
        return (new Selection(), deposits);
    }

    private static Order Single(IReadOnlyList<Order> orders) => Assert.Single(orders);

    // ---------- selection

    [Fact]
    public void Clicking_your_unit_selects_it_and_shift_click_adds_or_removes()
    {
        var state = State();
        var (sel, _) = Fresh(state);

        sel.Click(state, new Pick(2, 1, 32), shift: false);
        Assert.Equal([32], sel.Units);
        sel.Click(state, new Pick(1, 1, 33), shift: true);
        Assert.Equal([32, 33], sel.Units);
        sel.Click(state, new Pick(2, 1, 32), shift: true);
        Assert.Equal([33], sel.Units);
    }

    [Fact]
    public void Clicking_a_tile_without_a_drawn_unit_takes_your_unit_on_it_first()
    {
        var state = State() with { Units = [.. State().Units, new UnitDto { Id = 51, Owner = 0, Type = UnitType.Scout, X = 2, Y = 1 }] };
        var (sel, _) = Fresh(state);

        sel.Click(state, new Pick(2, 1), shift: false);

        Assert.Equal([31], sel.Units);
    }

    [Fact]
    public void Clicking_your_building_selects_it_and_enemies_are_only_inspected()
    {
        var state = State();
        var (sel, _) = Fresh(state);

        sel.Click(state, new Pick(2, 0), shift: false);
        Assert.Equal(77, sel.Building);
        Assert.Empty(sel.Units);

        sel.Click(state, new Pick(3, 1, 50), shift: false);
        Assert.Null(sel.Building);
        Assert.Empty(sel.Units);
        Assert.Equal(50, sel.Inspected);

        sel.Click(state, new Pick(0, 0), shift: false);
        Assert.Equal(212, sel.Inspected); // a remembered building can be looked at

        sel.Click(state, new Pick(0, 1), shift: false);
        Assert.True(sel.IsEmpty);
    }

    [Fact]
    public void A_drag_box_takes_only_your_units()
    {
        var state = State();
        var (sel, _) = Fresh(state);

        sel.Box(state, [31, 33, 50], shift: false);
        Assert.Equal([31, 33], sel.Units);

        sel.Box(state, [32], shift: true);
        Assert.Equal([31, 32, 33], sel.Units);

        sel.Box(state, [50], shift: false); // nothing of ours in it
        Assert.True(sel.IsEmpty);
    }

    [Fact]
    public void Double_click_selects_every_unit_of_that_type()
    {
        var state = State();
        var (sel, _) = Fresh(state);

        sel.SelectType(state, new Pick(2, 1, 31), shift: false);

        Assert.Equal([31, 32], sel.Units);
    }

    [Fact]
    public void Dead_units_drop_out_of_the_selection()
    {
        var state = State();
        var (sel, _) = Fresh(state);
        sel.Box(state, [31, 32, 33], shift: false);

        sel.Prune(state with { Units = state.Units.Where(u => u.Id != 32).ToList() });

        Assert.Equal([31, 33], sel.Units);
    }

    // ---------- right-click

    [Fact]
    public void Right_clicking_ground_moves_the_selection()
    {
        var state = State();
        var (sel, deposits) = Fresh(state);
        sel.Box(state, [31, 33], shift: false);

        var order = Single(OrderBuilder.Smart(state, deposits, sel, new Pick(0, 1), Lore.Classic));

        Assert.Equal(CommandType.Move, order.Command.Type);
        Assert.Equal([31, 33], order.Command.UnitIds!);
        Assert.Equal((0, 1), (order.Command.X, order.Command.Y));
        Assert.Equal("2 units: move to (0, 1)", order.Label);
    }

    [Fact]
    public void Right_clicking_a_deposit_sends_workers_to_mine_and_the_rest_to_walk()
    {
        var state = State();
        var (sel, deposits) = Fresh(state);
        sel.Box(state, [31, 32, 33], shift: false);

        var orders = OrderBuilder.Smart(state, deposits, sel, new Pick(3, 0), Lore.Classic);

        Assert.Equal(2, orders.Count);
        Assert.Equal((CommandType.Gather, 4), (orders[0].Command.Type, orders[0].Command.TargetId));
        Assert.Equal([31, 32], orders[0].Command.UnitIds!);
        Assert.Equal(CommandType.Move, orders[1].Command.Type);
        Assert.Equal([33], orders[1].Command.UnitIds!);
    }

    [Fact]
    public void A_deposit_seen_earlier_can_still_be_mined_from_the_fog()
    {
        var state = State();
        var (sel, deposits) = Fresh(state);
        sel.Click(state, new Pick(2, 1, 31), shift: false);
        var fogged = state with { Resources = [], Visibility = ["0110", "0111"] }; // (3,0) is fogged now
        deposits.Update(fogged);

        var order = Single(OrderBuilder.Smart(fogged, deposits, sel, new Pick(3, 0), Lore.Classic));

        Assert.Equal(CommandType.Gather, order.Command.Type);
        Assert.Equal(4, order.Command.TargetId);
    }

    [Fact]
    public void A_deposit_seen_gone_is_forgotten()
    {
        var state = State();
        var (sel, deposits) = Fresh(state);
        sel.Click(state, new Pick(2, 1, 31), shift: false);
        deposits.Update(state with { Resources = [] }); // (3,0) is in sight and empty

        Assert.Equal(CommandType.Move, Single(OrderBuilder.Smart(state with { Resources = [] }, deposits, sel, new Pick(3, 0), Lore.Classic)).Command.Type);
    }

    [Fact]
    public void Right_clicking_an_enemy_attacks_it_with_everyone()
    {
        var state = State();
        var (sel, deposits) = Fresh(state);
        sel.Box(state, [31, 33], shift: false);

        var order = Single(OrderBuilder.Smart(state, deposits, sel, new Pick(3, 1, 50), Lore.Classic));

        Assert.Equal(CommandType.Attack, order.Command.Type);
        Assert.Equal(50, order.Command.TargetId);
        Assert.Equal([31, 33], order.Command.UnitIds!);
        Assert.Null(order.Command.X);
    }

    [Fact]
    public void A_remembered_building_can_only_be_attack_moved_to()
    {
        var state = State();
        var (sel, deposits) = Fresh(state);
        sel.Click(state, new Pick(1, 1, 33), shift: false);

        var order = Single(OrderBuilder.Smart(state, deposits, sel, new Pick(0, 0), Lore.Classic));

        Assert.Equal(CommandType.Attack, order.Command.Type);
        Assert.Null(order.Command.TargetId); // the API refuses an id it can't see
        Assert.Equal((0, 0), (order.Command.X, order.Command.Y));
    }

    [Fact]
    public void Workers_right_clicked_onto_your_construction_site_help_build_it()
    {
        var state = State();
        var (sel, deposits) = Fresh(state);
        sel.Box(state, [31, 32], shift: false);

        var order = Single(OrderBuilder.Smart(state, deposits, sel, new Pick(1, 0), Lore.Classic));

        Assert.Equal(CommandType.Build, order.Command.Type);
        Assert.Equal(BuildingType.Barracks, order.Command.BuildingType);
        Assert.Equal((1, 0), (order.Command.X, order.Command.Y));
    }

    [Fact]
    public void Right_clicking_with_a_building_selected_sets_its_rally_point()
    {
        var state = State();
        var (sel, deposits) = Fresh(state);
        sel.Click(state, new Pick(2, 0), shift: false);

        var order = Single(OrderBuilder.Smart(state, deposits, sel, new Pick(0, 1), Lore.Classic));

        Assert.Equal(new CommandRequest { Type = CommandType.Rally, BuildingId = 77, X = 0, Y = 1 }, order.Command);
    }

    [Fact]
    public void Nothing_selected_means_no_orders()
    {
        var state = State();
        var (sel, deposits) = Fresh(state);

        Assert.Empty(OrderBuilder.Smart(state, deposits, sel, new Pick(0, 1), Lore.Classic));
        Assert.Empty(OrderBuilder.Stop(state, sel, Lore.Classic));
        Assert.Empty(OrderBuilder.AttackMove(state, sel, 0, 1, Lore.Classic));
    }

    [Fact]
    public void Stop_and_attack_move_use_the_selected_units()
    {
        var state = State();
        var (sel, _) = Fresh(state);
        sel.Box(state, [31, 33], shift: false);

        var stop = Single(OrderBuilder.Stop(state, sel, Lore.Classic)).Command;
        Assert.Equal(CommandType.Stop, stop.Type);
        Assert.Equal([31, 33], stop.UnitIds!);
        var attack = Single(OrderBuilder.AttackMove(state, sel, 0, 1, Lore.Classic)).Command;
        Assert.Equal((CommandType.Attack, 0, 1), (attack.Type, attack.X, attack.Y));
        Assert.Equal([31, 33], attack.UnitIds!);
    }
}
