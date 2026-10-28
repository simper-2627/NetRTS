using NetRts.Engine;
using NetRts.PlayClient.Game;
using NetRts.Protocol;

namespace NetRts.PlayClient.Tests;

public class CommandCardTests
{
    private static readonly RulesDto Rules = GameRules.ToDto();

    private static readonly MapDto Map = new() { Width = 4, Height = 2, Terrain = [".#..", "...."] };

    // Us (slot 1): CommandCenter #30 at (3,1) training a worker, TechLab #77 at (2,0) researching Armor1.
    private static GameStateDto State() => StateAdapterTests.SampleState();

    private static BuildingDto Building(GameStateDto s, int id) => s.Buildings.Single(b => b.Id == id);

    private static CardButton Button(IEnumerable<CardButton> buttons, CardAction action) => buttons.Single(b => b.Action == action);

    [Fact]
    public void Build_buttons_show_cost_hotkey_and_what_is_missing()
    {
        var buttons = CommandCard.BuildButtons(State(), Rules, budget: 380);

        Assert.Equal(Rules.Buildings.Count, buttons.Count);
        var barracks = Button(buttons, new BuildAction(BuildingType.Barracks));
        Assert.Equal(('B', 150, (string?)null), (barracks.Hotkey, barracks.Cost, barracks.Blocked));
        Assert.Equal("Needs a finished Barracks", Button(buttons, new BuildAction(BuildingType.GuardTower)).Blocked);
        Assert.Equal("Not enough ore", Button(buttons, new BuildAction(BuildingType.CommandCenter)).Blocked); // 400 > 380
    }

    [Fact]
    public void The_budget_not_the_bank_decides_what_is_affordable()
    {
        var buttons = CommandCard.BuildButtons(State(), Rules, budget: 120, new Lore(new LoreTables { Snow = true, Ore = "grubs", BuildingNames = new() { ["Barracks"] = "Housing" } }));

        Assert.Equal("Not enough grubs", Button(buttons, new BuildAction(BuildingType.Barracks)).Blocked);
        Assert.Null(Button(buttons, new BuildAction(BuildingType.ResourceDepot)).Blocked);
    }

    [Fact]
    public void A_command_center_trains_workers_until_its_queue_is_full()
    {
        var state = State();
        var cc = Building(state, 30);

        var worker = Assert.Single(CommandCard.BuildingButtons(state, Rules, cc, budget: 380));
        Assert.Equal((new ProduceAction(UnitType.Worker), 'W', 50, (string?)null), (worker.Action, worker.Hotkey, worker.Cost, worker.Blocked));

        var full = cc with { Production = [.. Enumerable.Repeat(new ProductionDto(UnitType.Worker, 0), GameRules.MaxProductionQueue)] };
        Assert.Equal($"Queue full ({GameRules.MaxProductionQueue})", Assert.Single(CommandCard.BuildingButtons(state, Rules, full, 380)).Blocked);

        var site = cc with { Completed = false };
        Assert.Equal("Still under construction", Assert.Single(CommandCard.BuildingButtons(state, Rules, site, 380)).Blocked);
    }

    [Fact]
    public void A_barracks_offers_soldiers_archers_and_scouts()
    {
        var state = State();
        var barracks = Building(state, 30) with { Id = 31, Type = BuildingType.Barracks, Production = [] };

        var buttons = CommandCard.BuildingButtons(state, Rules, barracks, budget: 100);

        Assert.Equal([UnitType.Soldier, UnitType.Archer, UnitType.Scout], buttons.Select(b => ((ProduceAction)b.Action).Type));
        Assert.Equal(['S', 'A', 'C'], buttons.Select(b => b.Hotkey));
        Assert.Equal("Not enough ore", Button(buttons, new ProduceAction(UnitType.Archer)).Blocked); // 125 > 100
    }

    [Fact]
    public void A_tech_lab_offers_the_next_tier_of_each_upgrade()
    {
        var state = State() with { You = State().You with { Upgrades = [UpgradeType.Weapons1], Researching = [] } };
        var lab = Building(state, 77) with { Research = null };

        var buttons = CommandCard.BuildingButtons(state, Rules, lab, budget: 1000);

        Assert.Equal([UpgradeType.Weapons2, UpgradeType.Armor1, UpgradeType.Mobility1, UpgradeType.Harvesting1, UpgradeType.SiegeEngineering],
            buttons.Select(b => ((ResearchAction)b.Action).Upgrade));
        Assert.Equal(['W', 'A', 'M', 'H', '?'], buttons.Select(b => b.Hotkey));
        Assert.All(buttons, b => Assert.Null(b.Blocked));
    }

    [Fact]
    public void Costs_come_from_your_researched_stats_when_the_state_has_them()
    {
        var state = State() with
        {
            You = State().You with
            {
                UnitStats = [new UnitStatsDto { Type = UnitType.Worker, Cost = 70 }],
                BuildingStats = [new BuildingStatsDto { Type = BuildingType.Barracks, Cost = 90 }],
            },
        };

        Assert.Equal(70, Assert.Single(CommandCard.BuildingButtons(state, Rules, Building(state, 30), budget: 380)).Cost);
        Assert.Equal(90, Button(CommandCard.BuildButtons(state, Rules, budget: 380), new BuildAction(BuildingType.Barracks)).Cost);
        Assert.Equal(400, Button(CommandCard.BuildButtons(state, Rules, budget: 380), new BuildAction(BuildingType.CommandCenter)).Cost);
    }

    [Fact]
    public void A_building_behind_research_says_which()
    {
        var state = State() with { You = State().You with { Upgrades = [] } }; // with a finished TechLab
        Assert.Equal("Needs Siege Engineering", Button(CommandCard.BuildButtons(state, Rules, 1000), new BuildAction(BuildingType.SiegeWorkshop)).Blocked);

        var researched = State() with { You = State().You with { Upgrades = [UpgradeType.SiegeEngineering] } };
        Assert.Null(Button(CommandCard.BuildButtons(researched, Rules, 1000), new BuildAction(BuildingType.SiegeWorkshop)).Blocked);
    }

    [Fact]
    public void One_research_at_a_time_per_lab_and_per_upgrade()
    {
        var state = State() with { You = State().You with { Researching = [new ResearchDto(UpgradeType.Mobility1, 10, 99)] } };

        var busy = CommandCard.BuildingButtons(state, Rules, Building(state, 77), budget: 1000);
        Assert.All(busy, b => Assert.Equal("Already researching here", b.Blocked));

        var idle = CommandCard.BuildingButtons(state, Rules, Building(state, 77) with { Research = null }, budget: 1000);
        Assert.Equal("Being researched elsewhere", Button(idle, new ResearchAction(UpgradeType.Mobility1)).Blocked);
    }

    [Fact]
    public void Hotkeys_are_case_insensitive()
    {
        var buttons = CommandCard.BuildButtons(State(), Rules, 380);

        Assert.Equal(new BuildAction(BuildingType.Barracks), CommandCard.ForKey(buttons, 'b')!.Action);
        Assert.Null(CommandCard.ForKey(buttons, 'z'));
    }

    // ---------- placement

    [Theory]
    [InlineData(1, 0, "Rock")]
    [InlineData(3, 0, "There's a deposit there")]
    [InlineData(2, 0, "There's a building there")]
    [InlineData(0, 0, "An enemy building was there")]
    [InlineData(4, 0, "Off the map")]
    [InlineData(0, 1, null)]
    public void Placement_checks_what_the_player_can_see(int x, int y, string? problem)
    {
        var state = State();
        var deposits = new DepositMemory();
        deposits.Update(state);

        Assert.Equal(problem, Placement.Problem(state, Map, deposits, Rules, BuildingType.Barracks, x, y, budget: 380));
    }

    [Fact]
    public void Placement_refuses_a_tile_with_a_visible_enemy_on_it()
    {
        var enemy = new UnitDto { Id = 51, Owner = 0, Type = UnitType.Scout, X = 0, Y = 1, Hp = 55, MaxHp = 55 };
        var state = State() with { Units = [.. State().Units, enemy] };

        Assert.Equal("An enemy unit is standing there", Placement.TileProblem(state, Map, new DepositMemory(), BuildingType.Barracks, 0, 1));
    }

    [Fact]
    public void Workers_may_be_sent_to_finish_their_own_site_of_the_same_type()
    {
        var site = new BuildingDto { Id = 90, Owner = 1, Type = BuildingType.Barracks, X = 0, Y = 1, Completed = false, ConstructionPercent = 30 };
        var state = State() with { Buildings = [.. State().Buildings, site] };

        Assert.Null(Placement.TileProblem(state, Map, new DepositMemory(), BuildingType.Barracks, 0, 1));
        Assert.Equal("There's a building there", Placement.TileProblem(state, Map, new DepositMemory(), BuildingType.ResourceDepot, 0, 1));
    }

    [Fact]
    public void The_ghost_mask_marks_every_tile_and_goes_all_red_when_unaffordable()
    {
        var state = State();
        var deposits = new DepositMemory();
        deposits.Update(state);

        Assert.Equal(["0000", "1110"], Placement.Mask(state, Map, deposits, Rules, BuildingType.Barracks, budget: 380));
        Assert.Equal(["0000", "0000"], Placement.Mask(state, Map, deposits, Rules, BuildingType.Barracks, budget: 10));
        Assert.Equal(["0000", "0000"], Placement.Mask(state, Map, deposits, Rules, BuildingType.GuardTower, budget: 1000)); // needs a Barracks
    }
}
