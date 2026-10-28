using System.Text.Json;
using System.Text.Json.Nodes;
using NetRts.Protocol;

namespace NetRts.Engine.Tests;

public class RulesFileTests
{
    [Fact]
    public void The_shipped_rules_file_loads_and_buildings_train_what_names_them_as_producedAt()
    {
        Assert.Equal([UnitType.Soldier, UnitType.Archer, UnitType.Scout], GameRules.Buildings[BuildingType.Barracks].Produces);
        Assert.Equal(11, GameRules.Upgrades[UpgradeType.Weapons1].Units[UnitType.Soldier]["damage"]);
        Assert.Equal(5, GameRules.StartingWorkers);
        Assert.Matches("^[0-9]+-[0-9a-f]{12}$", GameRules.Version);
    }

    [Fact]
    public void An_upgrade_gives_units_on_the_field_the_new_stats_and_keeps_their_share_of_hp()
    {
        var sim = TestGames.New(rules: DrilledSoldiers());
        var hurt = sim.SpawnUnit(0, UnitType.Soldier, 10, 10);
        var fresh = sim.SpawnUnit(0, UnitType.Soldier, 10, 11);
        sim.SetHp(hurt, 25);

        sim.GrantUpgrade(0, "Drill");

        Assert.Equal(50, sim.Find(hurt)!.Value.Hp);
        Assert.Equal(100, sim.Find(fresh)!.Value.Hp);
        var soldier = sim.GetPlayerView(0).Units.First(u => u.Id == hurt);
        Assert.Equal(100, soldier.MaxHp);
    }

    [Fact]
    public void Units_trained_after_an_upgrade_cost_and_have_the_new_stats()
    {
        var sim = TestGames.New(rules: DrilledSoldiers());
        var barracks = sim.SpawnBuilding(0, BuildingType.Barracks, 12, 12);
        sim.GrantUpgrade(0, "Drill");
        sim.SetResources(0, 100);

        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Produce, BuildingId = barracks, UnitType = UnitType.Soldier }).AssertAccepted();
        sim.Run(GameRules.Units[UnitType.Soldier].BuildTicks + 1);

        Assert.Equal(85, sim.Resources(0));
        var trained = sim.UnitIds(0, UnitType.Soldier).Single();
        Assert.Equal(100, sim.Find(trained)!.Value.Hp);
        var stats = sim.GetPlayerView(0).You.UnitStats.Single(u => u.Type == UnitType.Soldier);
        Assert.Equal((15, 15, 100), (stats.Cost, stats.Damage, stats.MaxHp));
        Assert.Equal(10, sim.GetPlayerView(1).You.UnitStats.Single(u => u.Type == UnitType.Soldier).Damage);
    }

    [Fact]
    public void Upgrades_change_buildings_too()
    {
        var sim = TestGames.New(rules: TestGames.Rules(r =>
            r["upgrades"]!["Fortify"] = JsonNode.Parse("""{ "cost": 0, "researchTicks": 1, "buildings": { "CommandCenter": { "maxHp": 3000 } } }""")));
        var cc = sim.CommandCenterId(0);
        sim.SetHp(cc, 750);

        sim.GrantUpgrade(0, "Fortify");

        Assert.Equal(1500, sim.Find(cc)!.Value.Hp);
        sim.GrantUpgrade(1, UpgradeType.Weapons1);
        Assert.Equal(12, sim.GetPlayerView(1).You.BuildingStats.Single(b => b.Type == BuildingType.GuardTower).Damage);
    }

    [Fact]
    public void When_two_upgrades_set_the_same_stat_the_one_researched_last_wins()
    {
        var sim = TestGames.New(rules: TestGames.Rules(r =>
        {
            r["upgrades"]!["Sharpen"] = JsonNode.Parse("""{ "cost": 0, "researchTicks": 1, "units": { "Soldier": { "damage": 20 } } }""");
            r["upgrades"]!["Hone"] = JsonNode.Parse("""{ "cost": 0, "researchTicks": 1, "units": { "Soldier": { "damage": 30 } } }""");
        }));

        sim.GrantUpgrade(0, "Hone");
        sim.GrantUpgrade(0, "Sharpen");
        sim.GrantUpgrade(1, "Sharpen");
        sim.GrantUpgrade(1, "Hone");

        Assert.Equal(20, sim.GetPlayerView(0).You.UnitStats.Single(u => u.Type == UnitType.Soldier).Damage);
        Assert.Equal(30, sim.GetPlayerView(1).You.UnitStats.Single(u => u.Type == UnitType.Soldier).Damage);
    }

    [Fact]
    public void A_unit_type_added_to_the_rules_file_can_be_trained_by_any_spelling_of_its_name()
    {
        var rules = TestGames.Rules(r => r["units"]!["Knight"] = JsonNode.Parse(
            """{ "cost": 200, "buildTicks": 5, "maxHp": 200, "damage": 15, "armor": 3, "range": 1, "attackCooldown": 1, "vision": 5, "speed": 12, "producedAt": "Barracks" }"""));
        var sim = TestGames.New(rules: rules);
        var barracks = sim.SpawnBuilding(0, BuildingType.Barracks, 12, 12);

        Assert.Contains(new UnitType("Knight"), rules.ToDto().Buildings.Single(b => b.Type == BuildingType.Barracks).Produces);
        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Produce, BuildingId = barracks, UnitType = "knight" }).AssertAccepted();
        Assert.Equal("MISSING_UNIT_TYPE", sim.SubmitOne(0, new CommandRequest { Type = CommandType.Produce, BuildingId = barracks, UnitType = "Dragon" }).AssertRejected());
        sim.Run(6);

        var knight = Assert.Single(sim.GetPlayerView(0).Units, u => u.Type == "Knight");
        Assert.Equal("Knight", knight.Type.Name);
        Assert.Equal(200, knight.MaxHp);
    }

    [Fact]
    public void Gather_and_build_take_any_unit_type_marked_as_a_worker()
    {
        var sim = TestGames.New(rules: TestGames.Rules(r => r["units"]!["Peasant"] = JsonNode.Parse(
            """{ "cost": 30, "buildTicks": 5, "maxHp": 30, "damage": 2, "armor": 0, "range": 1, "attackCooldown": 1, "vision": 4, "speed": 8, "isWorker": true, "carryCapacity": 5, "gatherPerTick": 1 }""")));
        var peasant = sim.SpawnUnit(0, "Peasant", 11, 10);
        var soldier = sim.SpawnUnit(0, UnitType.Soldier, 11, 11);
        var deposit = sim.SpawnDeposit(12, 10, 100);

        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Gather, UnitIds = [peasant], TargetId = deposit }).AssertAccepted();
        Assert.Equal("NO_MATCHING_UNITS", sim.SubmitOne(0, new CommandRequest { Type = CommandType.Gather, UnitIds = [soldier], TargetId = deposit }).AssertRejected());
        sim.Run(2);

        Assert.Equal(98, sim.Find(deposit)!.Value.Hp); // its own gatherPerTick: 1 a tick
    }

    [Fact]
    public void A_broken_rules_file_is_refused_with_every_problem_listed()
    {
        var error = Assert.Throws<InvalidDataException>(() => TestGames.Rules(r =>
        {
            r["units"]!["Soldier"]!["producedAt"] = "Stable";
            r["buildings"]!["TechLab"]!["requires"] = new JsonArray("barracks");
            r["upgrades"]!["Weapons1"]!["units"]!["Soldier"] = JsonNode.Parse("""{ "attack": 15 }""");
            r["units"]!["Scout"]!["maxHp"] = 0;
        }));

        Assert.Contains("units.Soldier.producedAt: there is no building \"Stable\"", error.Message);
        Assert.Contains("buildings.TechLab.requires: there is no building \"barracks\" (did you mean \"Barracks\"?)", error.Message);
        Assert.Contains("upgrades.Weapons1.units.Soldier.attack: upgrades can only set", error.Message);
        Assert.Contains("units.Scout.maxHp is 0", error.Message);
    }

    [Fact]
    public void A_misspelled_stat_is_refused()
    {
        var error = Assert.Throws<InvalidDataException>(() => TestGames.Rules(r => r["units"]!["Soldier"]!["damge"] = 9));

        Assert.Contains("damge", error.Message);
    }

    [Fact]
    public void Type_names_travel_as_plain_strings()
    {
        var json = JsonSerializer.Serialize(new CommandRequest { Type = CommandType.Produce, UnitType = UnitType.Worker }, JsonSerializerOptions.Web);
        Assert.Contains("\"unitType\":\"Worker\"", json);

        var parsed = JsonSerializer.Deserialize<CommandRequest>("""{ "type": "Build", "buildingType": "barracks" }""", JsonSerializerOptions.Web)!;
        Assert.Equal(BuildingType.Barracks, parsed.BuildingType);

        var stats = new PlayerMatchStatsDto { UnitsTrained = new Dictionary<UnitType, int> { [UnitType.Soldier] = 3 } };
        var roundTrip = JsonSerializer.Deserialize<PlayerMatchStatsDto>(JsonSerializer.Serialize(stats, JsonSerializerOptions.Web), JsonSerializerOptions.Web)!;
        Assert.Equal(3, roundTrip.UnitsTrained[UnitType.Soldier]);
    }

    /// <summary>The example from the design: a 10/10/50 soldier that a "Drill" upgrade makes 15/15/100.</summary>
    private static RuleSet DrilledSoldiers() => TestGames.Rules(r =>
    {
        var soldier = r["units"]!["Soldier"]!;
        soldier["damage"] = 10;
        soldier["cost"] = 10;
        soldier["maxHp"] = 50;
        r["upgrades"]!["Drill"] = JsonNode.Parse("""{ "cost": 0, "researchTicks": 1, "units": { "Soldier": { "damage": 15, "cost": 15, "maxHp": 100 } } }""");
    });
}
