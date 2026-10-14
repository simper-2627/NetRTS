using System.Net.Http.Json;
using NetRts.Bots;
using NetRts.Protocol;

namespace NetRts.PlayClient.Game;

/// <summary>
/// The match-level calls <see cref="NetRtsClient"/> doesn't wrap: speed and single steps. The server
/// allows them only in matches with at most one real player (you against house bots), which
/// <see cref="MatchSummaryDto.SpeedAdjustable"/> says.
/// </summary>
public sealed class MatchControls(HttpClient http, Guid matchId)
{
    public static readonly int[] Speeds = [100, 250, 500, 1000, 2000];

    public Task<MatchSummaryDto> SetSpeedAsync(int tickIntervalMs, CancellationToken ct = default) =>
        PostAsync("speed", new MatchSpeedRequest { TickIntervalMs = tickIntervalMs }, ct);

    public Task<MatchSummaryDto> SetPausedAsync(bool paused, CancellationToken ct = default) =>
        PostAsync("speed", new MatchSpeedRequest { Paused = paused }, ct);

    /// <summary>Runs one tick of a paused match.</summary>
    public Task<MatchSummaryDto> StepAsync(CancellationToken ct = default) => PostAsync("step", null, ct);

    private async Task<MatchSummaryDto> PostAsync(string path, object? body, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync($"api/v1/matches/{matchId}/{path}", body, NetRtsClient.JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>(NetRtsClient.JsonOptions, ct);
            throw new NetRtsApiException(response.StatusCode, error?.Code ?? response.StatusCode.ToString(), error?.Message ?? response.ReasonPhrase ?? "");
        }

        return (await response.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, ct))!;
    }
}
