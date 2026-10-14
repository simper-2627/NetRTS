using NetRts.PlayClient.Game;
using NetRts.Protocol;

namespace NetRts.PlayClient.Tests;

/// <summary>Keyboard and click handling end to end, minus the JS view: input → orders on the fake server.</summary>
public class PlayControllerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static async Task<(FakeServer Server, GameSession Session, PlayController Controller, Task Run)> StartAsync(Func<GameStateDto, GameStateDto>? shape = null)
    {
        var server = new FakeServer(lastTick: 3) { Shape = shape ?? (s => s) };
        var session = new GameSession(server.Client(), server.MatchId, delay: (_, _) => Task.CompletedTask);
        var controller = new PlayController(session);
        var run = session.RunAsync(TestContext.Current.CancellationToken);
        await server.Polling(1).WaitAsync(Timeout, TestContext.Current.CancellationToken); // tick 0 is on screen
        await controller.OnStateAsync(session.State!);
        return (server, session, controller, run);
    }

    private static async Task FinishAsync(FakeServer server, Task run)
    {
        for (var t = 1; t <= 3; t++)
        {
            server.Advance(t);
        }

        await run.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    private static IEnumerable<CommandRequest> Sent(FakeServer server) => server.Submitted.SelectMany(s => s.Commands);

    [Fact]
    public async Task Hotkeys_on_a_selected_building_train_units()
    {
        var (server, session, controller, run) = await StartAsync();
        await controller.OnClick(3, 1, null, shift: false, ctrl: false);   // our Command Center
        Assert.Equal(30, controller.Selection.Building);

        await controller.OnKey("w", ctrl: false, shift: false);

        var produce = Assert.Single(Sent(server));
        Assert.Equal((CommandType.Produce, 30, UnitType.Worker, 1), (produce.Type, produce.BuildingId!.Value, produce.UnitType!.Value, produce.Count!.Value));
        await FinishAsync(server, run);
    }

    [Fact]
    public async Task Shift_trains_five_as_far_as_the_queue_and_the_budget_allow()
    {
        var (server, session, controller, run) = await StartAsync();
        await controller.OnClick(3, 1, null, false, false);

        await controller.OnKey("W", ctrl: false, shift: true);

        // One worker is already queued (4 places left), and 380 ore buys seven.
        Assert.Equal(4, Assert.Single(Sent(server)).Count);
        Assert.Equal(380 - 4 * 50, controller.Budget);
        await FinishAsync(server, run);
    }

    [Fact]
    public async Task Orders_this_tick_spend_from_the_budget_before_the_server_answers()
    {
        var (server, session, controller, run) = await StartAsync();
        await controller.OnClick(3, 1, null, false, false);
        await controller.OnKey("W", false, shift: true);     // 4 workers: 200 of 380
        await controller.OnClick(2, 1, 31, false, false);
        await controller.OnKey("b", false, false);
        await controller.OnClick(0, 1, null, false, false);   // a Barracks: 150 more

        Assert.Equal(30, controller.Budget);
        await controller.OnKey("b", false, false);           // the bank still says 380; the budget knows better
        Assert.Equal("Not enough ore", controller.Notice);
        Assert.Null(controller.Mode);
        Assert.Equal(1, session.Orders.Count);                // the Barracks waits for the next tick's request
        await FinishAsync(server, run);
    }

    [Fact]
    public async Task Building_placement_sends_the_selected_workers()
    {
        var (server, session, controller, run) = await StartAsync();
        await controller.OnClick(2, 1, 31, false, false);   // worker #31

        await controller.OnKey("b", false, false);
        Assert.Equal(("build", BuildingType.Barracks), (controller.Mode, controller.Placing!.Value));

        await controller.OnClick(1, 0, null, false, false); // rock: refused here, still placing
        Assert.Equal("Rock", controller.Notice);
        Assert.Equal("build", controller.Mode);
        Assert.Empty(server.Submitted);

        await controller.OnClick(0, 1, null, false, false);
        var build = Assert.Single(Sent(server));
        Assert.Equal((CommandType.Build, BuildingType.Barracks, 0, 1), (build.Type, build.BuildingType!.Value, build.X!.Value, build.Y!.Value));
        Assert.Equal([31], build.UnitIds!);
        Assert.Null(controller.Mode);
        Assert.Equal(380 - 150, controller.Budget);
        await FinishAsync(server, run);
    }

    [Fact]
    public async Task Hover_explains_why_a_tile_is_refused()
    {
        var (server, session, controller, run) = await StartAsync();
        await controller.OnClick(2, 1, 31, false, false);
        await controller.OnKey("d", false, false);   // Resource Depot

        controller.OnHover(3, 0);
        Assert.Equal("There's a deposit there", controller.PlacementProblem);
        controller.OnHover(0, 1);
        Assert.Null(controller.PlacementProblem);

        await controller.OnKey("Escape", false, false);
        Assert.Null(controller.Mode);
        await FinishAsync(server, run);
    }

    [Fact]
    public async Task Blocked_buttons_explain_instead_of_sending()
    {
        var (server, session, controller, run) = await StartAsync();
        await controller.OnClick(2, 1, 31, false, false);

        await controller.OnKey("g", false, false);   // Guard Tower needs a Barracks

        Assert.Equal("Needs a finished Barracks", controller.Notice);
        Assert.Null(controller.Mode);
        await FinishAsync(server, run);
    }

    [Fact]
    public async Task Control_groups_are_set_with_ctrl_and_recalled_with_the_digit()
    {
        var (server, session, controller, run) = await StartAsync();
        await controller.OnClick(2, 1, 31, false, false);
        await controller.OnKey("1", ctrl: true, shift: false);
        await controller.OnClick(3, 1, null, false, false);   // the CC
        await controller.OnKey("2", ctrl: true, shift: false);

        await controller.OnKey("Escape", false, false);
        Assert.True(controller.Selection.IsEmpty);

        await controller.OnKey("1", false, false);
        Assert.Equal([31], controller.Selection.Units);
        await controller.OnKey("2", false, false);
        Assert.Equal(30, controller.Selection.Building);
        await FinishAsync(server, run);
    }

    [Fact]
    public async Task A_and_S_are_attack_move_and_stop_for_units()
    {
        var (server, session, controller, run) = await StartAsync();
        await controller.OnClick(2, 1, 31, false, false);

        await controller.OnKey("a", false, false);
        Assert.Equal("attack", controller.Mode);
        await controller.OnClick(0, 1, null, false, false);
        Assert.Null(controller.Mode);
        await controller.OnKey("s", false, false);

        var attack = Assert.Single(Sent(server));
        Assert.Equal((CommandType.Attack, 0, 1), (attack.Type, attack.X!.Value, attack.Y!.Value));
        Assert.Equal(CommandType.Stop, Assert.Single(session.Orders.Pending).Command.Type); // next tick's request
        await FinishAsync(server, run);
    }

    [Fact]
    public async Task The_idle_worker_button_cycles_through_idle_workers()
    {
        static UnitDto Worker(int id, UnitActivity activity, int owner = 1) =>
            new() { Id = id, Owner = owner, Type = UnitType.Worker, X = 0, Y = 1, Hp = 40, MaxHp = 40, Activity = activity };
        var (server, session, controller, run) = await StartAsync(s => s with
        {
            Units = [Worker(31, UnitActivity.Gathering), Worker(35, UnitActivity.Idle), Worker(32, UnitActivity.Idle),
                     Worker(40, UnitActivity.Idle, owner: 0), new UnitDto { Id = 33, Owner = 1, Type = UnitType.Soldier, Activity = UnitActivity.Idle }],
        });

        Assert.Equal([32, 35], controller.IdleWorkers().Select(u => u.Id)); // yours, workers, idle

        await controller.JumpToIdleWorkerAsync();
        Assert.Equal([32], controller.Selection.Units);
        await controller.OnKey("i", false, false);
        Assert.Equal([35], controller.Selection.Units);
        await controller.JumpToIdleWorkerAsync();
        Assert.Equal([32], controller.Selection.Units); // round again
        Assert.Empty(server.Submitted);                // looking isn't ordering
        await FinishAsync(server, run);
    }

    [Fact]
    public async Task No_idle_workers_means_the_button_does_nothing()
    {
        var (server, session, controller, run) = await StartAsync(); // the sample's only worker is gathering

        Assert.Empty(controller.IdleWorkers());
        await controller.JumpToIdleWorkerAsync();
        Assert.True(controller.Selection.IsEmpty);
        await FinishAsync(server, run);
    }
}
