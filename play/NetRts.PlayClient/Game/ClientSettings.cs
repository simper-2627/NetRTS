using Microsoft.JSInterop;

namespace NetRts.PlayClient.Game;

/// <summary>
/// The server address and the player's API key, kept in this browser's localStorage so a page reload
/// (or tomorrow's lab) finds them again.
/// </summary>
public sealed class ClientSettings(IJSRuntime js, string defaultServer)
{
    public const string FallbackServer = "http://localhost:5080";

    private const string ServerKey = "netrts-play.server";
    private const string ApiKeyKey = "netrts-play.apiKey";
    private const string NameKey = "netrts-play.name";

    private bool _loaded;

    public string DefaultServer { get; } = defaultServer;

    public string Server { get; private set; } = defaultServer;

    public string? ApiKey { get; private set; }

    /// <summary>The player name the key was registered under, for display only.</summary>
    public string? PlayerName { get; private set; }

    public event Action? Changed;

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        Server = NormalizeServer(await GetAsync(ServerKey)) ?? DefaultServer;
        ApiKey = Blank(await GetAsync(ApiKeyKey));
        PlayerName = Blank(await GetAsync(NameKey));
        _loaded = true;
    }

    public async Task SetServerAsync(string server)
    {
        var normalized = NormalizeServer(server) ?? DefaultServer;
        if (normalized == Server)
        {
            return;
        }

        Server = normalized;
        await SetAsync(ServerKey, normalized == DefaultServer ? null : normalized);
        Changed?.Invoke();
    }

    public async Task SetPlayerAsync(string? apiKey, string? name)
    {
        ApiKey = Blank(apiKey);
        PlayerName = Blank(name);
        await SetAsync(ApiKeyKey, ApiKey);
        await SetAsync(NameKey, PlayerName);
        Changed?.Invoke();
    }

    /// <summary>"localhost:5080/" → "http://localhost:5080"; null for blank or unusable input.</summary>
    public static string? NormalizeServer(string? server)
    {
        var s = server?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }

        if (!s.Contains("://", StringComparison.Ordinal))
        {
            s = "http://" + s;
        }

        return Uri.TryCreate(s, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/')
            : null;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private ValueTask<string?> GetAsync(string key) => js.InvokeAsync<string?>("localStorage.getItem", key);

    private async Task SetAsync(string key, string? value)
    {
        if (value is null)
        {
            await js.InvokeVoidAsync("localStorage.removeItem", key);
        }
        else
        {
            await js.InvokeVoidAsync("localStorage.setItem", key, value);
        }
    }
}
