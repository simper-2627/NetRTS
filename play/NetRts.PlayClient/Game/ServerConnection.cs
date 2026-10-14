using NetRts.Bots;

namespace NetRts.PlayClient.Game;

/// <summary>
/// The <see cref="NetRtsClient"/> for the current server and API key: the same SDK class bots use,
/// so a person plays through exactly the calls a bot makes.
/// </summary>
public sealed class ServerConnection : IDisposable
{
    private readonly ClientSettings _settings;
    private HttpClient? _http;
    private NetRtsClient? _client;
    private (string Server, string? Key) _current;

    public ServerConnection(ClientSettings settings)
    {
        _settings = settings;
        _settings.Changed += Reset;
    }

    public ClientSettings Settings => _settings;

    public Uri BaseAddress => new(_settings.Server.TrimEnd('/') + "/");

    /// <summary>The client for the current server, signed in with the stored key if there is one.</summary>
    public NetRtsClient Client
    {
        get
        {
            var wanted = (_settings.Server, _settings.ApiKey);
            if (_client is null || _current != wanted)
            {
                Reset();
                _http = new HttpClient { BaseAddress = BaseAddress };
                _client = new NetRtsClient(_http, _settings.ApiKey);
                _current = wanted;
            }

            return _client;
        }
    }

    /// <summary>The raw HTTP client behind <see cref="Client"/>, for the few calls the SDK doesn't wrap (speed, spectate).</summary>
    public HttpClient Http
    {
        get
        {
            _ = Client;
            return _http!;
        }
    }

    public void Dispose()
    {
        _settings.Changed -= Reset;
        Reset();
    }

    private void Reset()
    {
        _client?.Dispose();
        _http?.Dispose();
        _client = null;
        _http = null;
    }
}
