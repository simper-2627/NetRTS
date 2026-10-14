using System.Text.Json;
using NetRts.PlayClient.Game;
using NetRts.Protocol;

namespace NetRts.PlayClient.Tests;

public class StateAdapterTests
{
    internal static GameStateDto SampleState(int tick = 42) => new()
    {
        MatchId = Guid.NewGuid(),
        Tick = tick,
        Status = MatchStatus.Active,
        MaxTicks = 1800,
        TickIntervalMs = 500,
        MapWidth = 4,
        MapHeight = 2,
        You = new SelfDto { Slot = 1, Name = "human", Resources = 380, StorageCapacity = 1500 },
        Players =
        [
            new PlayerSummaryDto { Slot = 0, Name = "house-sitter", Score = new ScoreDto() },
            new PlayerSummaryDto { Slot = 1, Name = "human", Score = new ScoreDto() },
        ],
        Visibility = ["0011", "0111"],
        Units =
        [
            new UnitDto { Id = 31, Owner = 1, Type = UnitType.Worker, X = 2, Y = 1, Hp = 40, MaxHp = 40, Activity = UnitActivity.Gathering, Carrying = 6, TargetId = 4 },
            new UnitDto { Id = 50, Owner = 0, Type = UnitType.Soldier, X = 3, Y = 0, Hp = 90, MaxHp = 110, Activity = UnitActivity.Attacking, TargetId = 31 },
        ],
        Buildings =
        [
            new BuildingDto
            {
                Id = 30, Owner = 1, Type = BuildingType.CommandCenter, X = 3, Y = 1, Hp = 1500, MaxHp = 1500, Completed = true,
                ConstructionPercent = 100, Production = [new ProductionDto(UnitType.Worker, 50)], Rally = new PositionDto(2, 1),
            },
            new BuildingDto
            {
                Id = 77, Owner = 1, Type = BuildingType.TechLab, X = 2, Y = 0, Hp = 300, MaxHp = 550, Completed = true,
                ConstructionPercent = 100, Research = new ResearchDto(UpgradeType.Armor1, 40, 77),
            },
        ],
        Resources = [new ResourceDepositDto { Id = 4, X = 3, Y = 0, Remaining = 940 }],
        RememberedBuildings =
        [
            new RememberedBuildingDto { Id = 212, Owner = 0, Type = BuildingType.Barracks, X = 0, Y = 0, Hp = 700, MaxHp = 700, Completed = true, LastSeenTick = 31 },
            new RememberedBuildingDto { Id = 213, Owner = 0, Type = BuildingType.GuardTower, X = 1, Y = 0, Hp = 50, MaxHp = 500, Completed = false, LastSeenTick = 30 },
        ],
        Events = [new GameEventDto { Tick = tick, Kind = "UnitProduced", Message = "Worker 88 ready at (8,8)", EntityId = 88 }],
    };

    [Fact]
    public void Units_and_deposits_pass_through_unchanged()
    {
        var state = SampleState();
        var render = StateAdapter.ToRenderState(state);

        Assert.Same(state.Units, render.Units);
        Assert.Same(state.Resources, render.Resources);
        Assert.Equal(42, render.Tick);
        Assert.Equal(500, render.TickIntervalMs);
        Assert.Equal(MatchStatus.Active, render.Status);
    }

    [Fact]
    public void Fog_is_drawn_from_your_own_visibility_only()
    {
        var render = StateAdapter.ToRenderState(SampleState());

        var player = Assert.Single(render.Players);
        Assert.Equal(1, player.Slot);
        Assert.Equal(["0011", "0111"], player.Visibility);
    }

    [Fact]
    public void Remembered_enemy_buildings_are_drawn_where_they_were_last_seen()
    {
        var render = StateAdapter.ToRenderState(SampleState());

        Assert.Equal([30, 77, 212, 213], render.Buildings.Select(b => b.Id));
        Assert.All(render.Buildings.Take(2), b => Assert.False(b.Remembered));
        var barracks = render.Buildings.Single(b => b.Id == 212);
        Assert.True(barracks.Remembered);
        Assert.Equal((0, 0, BuildingType.Barracks, 0), (barracks.X, barracks.Y, barracks.Type, barracks.Owner));
        Assert.Equal(100, barracks.ConstructionPercent);
        Assert.Equal(31, barracks.LastSeenTick);
        Assert.Equal(0, render.Buildings.Single(b => b.Id == 213).ConstructionPercent); // last seen unfinished
    }

    [Fact]
    public void A_remembered_building_never_doubles_a_visible_one()
    {
        var state = SampleState() with
        {
            RememberedBuildings = [new RememberedBuildingDto { Id = 30, Owner = 1, Type = BuildingType.CommandCenter, X = 3, Y = 1, Completed = true }],
        };

        Assert.Single(StateAdapter.ToRenderState(state).Buildings, b => b.Id == 30);
    }

    [Fact]
    public void Visible_buildings_keep_production_research_and_rally()
    {
        var render = StateAdapter.ToRenderState(SampleState());

        var cc = render.Buildings.Single(b => b.Id == 30);
        Assert.Equal(new ProductionDto(UnitType.Worker, 50), Assert.Single(cc.Production));
        Assert.Equal(new PositionDto(2, 1), cc.Rally);
        Assert.Equal(new ResearchDto(UpgradeType.Armor1, 40, 77), render.Buildings.Single(b => b.Id == 77).Research);
    }

    /// <summary>
    /// The JS interop serializes like the server (camelCase, enums as strings). These are the names
    /// mapview.js reads; renaming any of them would silently break the map.
    /// </summary>
    [Fact]
    public void Serialized_state_has_the_shape_mapview_reads()
    {
        var json = JsonSerializer.Serialize(StateAdapter.ToRenderState(SampleState()), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(500, root.GetProperty("tickIntervalMs").GetInt32());
        var unit = root.GetProperty("units")[0];
        foreach (var name in new[] { "id", "owner", "x", "y", "hp", "maxHp", "carrying", "targetId" })
        {
            Assert.True(unit.TryGetProperty(name, out _), name);
        }

        Assert.Equal("Worker", unit.GetProperty("type").GetString());
        Assert.Equal("Gathering", unit.GetProperty("activity").GetString());

        var cc = root.GetProperty("buildings")[0];
        Assert.Equal("CommandCenter", cc.GetProperty("type").GetString());
        Assert.True(cc.GetProperty("completed").GetBoolean());
        Assert.Equal(100, cc.GetProperty("constructionPercent").GetInt32());
        Assert.Equal(50, cc.GetProperty("production")[0].GetProperty("percent").GetInt32());
        Assert.Equal(40, root.GetProperty("buildings")[1].GetProperty("research").GetProperty("percent").GetInt32());
        Assert.True(root.GetProperty("buildings")[2].GetProperty("remembered").GetBoolean());

        Assert.Equal(940, root.GetProperty("resources")[0].GetProperty("remaining").GetInt32());
        var player = root.GetProperty("players")[0];
        Assert.Equal(1, player.GetProperty("slot").GetInt32());
        Assert.Equal("0011", player.GetProperty("visibility")[0].GetString());
    }
}
