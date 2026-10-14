using NetRts.Bots;
using NetRts.PlayClient.Game;
using NetRts.Protocol;

namespace NetRts.PlayClient.Pages;

public partial class Home
{
    private readonly MatchSetup setup = new();
    private IReadOnlyList<HouseBotDto>? houseBots;
    private PlayerProfileDto? me;
    private List<MatchSummaryDto> waiting = [];
    private List<MatchSummaryDto> mine = [];
    private string serverInput = "";
    private string nameInput = "";
    private string keyInput = "";
    private string joinInput = "";
    private string? newKey;
    private string? serverError;
    private string? playerError;
    private string? createError;
    private string? joinError;
    private bool busy;

    private ClientSettings Settings => Server.Settings;

    protected override async Task OnInitializedAsync()
    {
        await Settings.LoadAsync();
        serverInput = Settings.Server;
        await ConnectAsync();
    }

    /// <summary>Talks to the server: house bots (proves it's reachable), then who the stored key belongs to.</summary>
    private async Task ConnectAsync()
    {
        serverError = playerError = null;
        houseBots = null;
        me = null;
        try
        {
            houseBots = await Server.Client.GetHouseBotsAsync();
        }
        catch (Exception e) when (e is HttpRequestException or NetRtsApiException or TaskCanceledException)
        {
            serverError = ApiErrors.Describe(e, Settings.Server);
            return;
        }

        if (Settings.ApiKey is not null)
        {
            try
            {
                me = await Server.Client.GetMeAsync();
                if (me.Name != Settings.PlayerName)
                {
                    await Settings.SetPlayerAsync(Settings.ApiKey, me.Name);
                }
            }
            catch (NetRtsApiException e) when (e.Status == System.Net.HttpStatusCode.Unauthorized)
            {
                playerError = $"The saved key isn't known to {Settings.Server}. Register or paste another key.";
            }
            catch (Exception e) when (e is HttpRequestException or NetRtsApiException or TaskCanceledException)
            {
                playerError = ApiErrors.Describe(e, Settings.Server);
            }
        }

        await RefreshMatchesAsync();
    }

    private async Task SaveServerAsync()
    {
        var normalized = ClientSettings.NormalizeServer(serverInput);
        if (normalized is null && !string.IsNullOrWhiteSpace(serverInput))
        {
            serverError = "That doesn't look like a web address, e.g. http://localhost:5080.";
            return;
        }

        await Settings.SetServerAsync(normalized ?? Settings.DefaultServer);
        serverInput = Settings.Server;
        await ConnectAsync();
    }

    private async Task RegisterAsync()
    {
        await RunAsync(async () =>
        {
            var response = await Server.Client.RegisterAsync(nameInput.Trim());
            await Settings.SetPlayerAsync(response.ApiKey, response.Name);
            newKey = response.ApiKey;
            nameInput = "";
            await ConnectAsync();
        }, e => playerError = e);
    }

    private async Task UseKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(keyInput))
        {
            playerError = "Paste the API key you got when you registered.";
            return;
        }

        await Settings.SetPlayerAsync(keyInput.Trim(), null);
        keyInput = "";
        newKey = null;
        await ConnectAsync();
    }

    private async Task ForgetKeyAsync()
    {
        await Settings.SetPlayerAsync(null, null);
        newKey = null;
        me = null;
        mine = [];
    }

    private async Task CreateAsync()
    {
        await RunAsync(async () =>
        {
            var match = await Server.Client.CreateMatchAsync(setup.ToRequest());
            Nav.NavigateTo($"play/{match.MatchId}");
        }, e => createError = e);
    }

    private async Task JoinAsync(Guid matchId)
    {
        await RunAsync(async () =>
        {
            await Server.Client.JoinMatchAsync(matchId);
            Nav.NavigateTo($"play/{matchId}");
        }, e => joinError = e);
    }

    private async Task JoinByKeyAsync()
    {
        var key = joinInput.Trim();
        if (key.Length == 0)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var match = Guid.TryParse(key, out var id)
                ? await Server.Client.GetMatchAsync(id)
                : await Server.Client.GetMatchByNameAsync(key);
            if (me is not null && match.Players.Any(p => p.PlayerId == me.PlayerId))
            {
                Nav.NavigateTo($"play/{match.MatchId}");
                return;
            }

            await Server.Client.JoinMatchAsync(match.MatchId);
            Nav.NavigateTo($"play/{match.MatchId}");
        }, e => joinError = e);
    }

    private async Task RefreshMatchesAsync()
    {
        joinError = null;
        try
        {
            var all = await Server.Client.ListMatchesAsync();
            var myId = me?.PlayerId;
            mine = all.Where(m => myId is not null && m.Status != MatchStatus.Completed && m.Players.Any(p => p.PlayerId == myId)).ToList();
            waiting = all.Where(m => m.Status == MatchStatus.Waiting && m.Heats is null && !mine.Contains(m)).ToList();
        }
        catch (Exception e) when (e is HttpRequestException or NetRtsApiException or TaskCanceledException)
        {
            joinError = ApiErrors.Describe(e, Settings.Server);
        }
    }

    private async Task RunAsync(Func<Task> action, Action<string> onError)
    {
        busy = true;
        playerError = createError = joinError = null;
        try
        {
            await action();
        }
        catch (Exception e) when (e is HttpRequestException or NetRtsApiException or TaskCanceledException)
        {
            onError(ApiErrors.Describe(e, Settings.Server));
        }
        finally
        {
            busy = false;
        }
    }

    private static string Describe(MatchSummaryDto m)
    {
        var who = m.Players.Count == 0 ? "empty" : string.Join(" vs ", m.Players.Select(p => p.Name));
        return m.Name is { } name ? $"{name}: {who}" : who;
    }
}
