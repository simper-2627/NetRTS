using NetRts.Protocol;

namespace NetRts.PlayClient.Game;

/// <summary>An order the player gave that hasn't been sent yet, with a short description for the HUD.</summary>
public sealed record PendingOrder(CommandRequest Command, string Label);

/// <summary>What the server said about a sent order: accepted (and queued) or rejected with a reason.</summary>
public sealed record OrderOutcome(int Tick, string Label, bool Accepted, long? CommandId, string? ErrorCode, string? Message);

/// <summary>
/// Orders wait here between clicks and the next submission. The session sends them all in one
/// <c>POST /commands</c>, at most once per tick, which keeps a person well inside the rate limits.
/// </summary>
public sealed class OrderQueue
{
    /// <summary>Most commands sent in one request; anything beyond waits for the next tick.</summary>
    public const int MaxPerRequest = 100;

    private readonly List<PendingOrder> _pending = [];

    public int Count => _pending.Count;

    public IReadOnlyList<PendingOrder> Pending => _pending;

    public void Add(CommandRequest command, string label)
    {
        ArgumentNullException.ThrowIfNull(command);
        _pending.Add(new PendingOrder(command, label));
    }

    /// <summary>Removes and returns the next batch to send (oldest first, at most <see cref="MaxPerRequest"/>).</summary>
    public IReadOnlyList<PendingOrder> TakeBatch()
    {
        var n = Math.Min(MaxPerRequest, _pending.Count);
        var batch = _pending.GetRange(0, n);
        _pending.RemoveRange(0, n);
        return batch;
    }

    /// <summary>Puts a batch that couldn't be sent back at the front, in order.</summary>
    public void Requeue(IReadOnlyList<PendingOrder> batch) => _pending.InsertRange(0, batch);

    public void Clear() => _pending.Clear();
}
