using NetRts.Engine;     // GameSimulation, GameConfig, PlayerSeat, GameRules, UnitStats, BuildingStats
using NetRts.Protocol;   // UnitType, BuildingType, UpgradeType, CommandRequest, CommandType

namespace Balance.Tests;

public class BalanceTests
{
    private static readonly Guid player1 = Guid.NewGuid();
    private static readonly Guid player2 = Guid.NewGuid();

    public static GameSimulation NewGame(int seed = 1, int maxTicks = 1800, int queueCapacity = 500, int commandsPerTick = 100) =>
        new(Guid.NewGuid(), new GameConfig { Seed = seed, MaxTicks = maxTicks, QueueCapacity = queueCapacity, CommandsPerTick = commandsPerTick },
        [new PlayerSeat(player1, "alpha"), new PlayerSeat(player2, "bravo")]);

    public static CommandRequest AttackTarget(int targetId, params int[] unitIds) =>
        new() { Type = CommandType.Attack, UnitIds = unitIds, TargetId = targetId };


    [Fact]
    public void GuardTower_vs_Soldier()
    {
        var sim = NewGame();

        var tower = sim.SpawnBuilding(0, BuildingType.GuardTower, 10, 10);      // slot, type, x, y, completed = true
        var soldier = sim.SpawnUnit(1, UnitType.Soldier, 13, 10);

        sim.Submit(1, [AttackTarget(tower, soldier)]);
        sim.Run(15);

        Assert.Null(sim.Find(soldier));
        Assert.True(sim.Find(tower)!.Value.Hp < 500);
    }
}
