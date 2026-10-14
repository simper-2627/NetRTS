using NetRts.Bots;

namespace NetRts.PlayClient.Game;

internal static class ApiErrors
{
    /// <summary>A one-line explanation of a failed API call, for the page.</summary>
    public static string Describe(Exception e, string server) => e switch
    {
        NetRtsApiException api when api.Code == "RATE_LIMITED" => "Too many requests; wait a moment and try again.",
        NetRtsApiException api when api.Status == System.Net.HttpStatusCode.Unauthorized =>
            "The server didn't accept the API key. Register again or paste the right key.",
        NetRtsApiException api => $"{Message(api)} ({api.Code})",
        HttpRequestException => $"Could not reach the server at {server}. Is it running, and is the address right?",
        TaskCanceledException => $"The server at {server} took too long to answer.",
        _ => e.Message,
    };

    /// <summary>The server's message without the "409 CODE: " prefix NetRtsApiException adds.</summary>
    private static string Message(NetRtsApiException e)
    {
        var prefix = $"{(int)e.Status} {e.Code}: ";
        return e.Message.StartsWith(prefix, StringComparison.Ordinal) ? e.Message[prefix.Length..] : e.Message;
    }
}
