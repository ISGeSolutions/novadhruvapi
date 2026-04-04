namespace Nova.Shared.Locking;

/// <summary>
/// Represents a held distributed lock. Dispose to release it.
/// </summary>
/// <remarks>
/// Always use in an <c>await using</c> block so the lock is released even if an
/// exception is thrown inside the critical section:
/// <code>
/// await using IDistributedLock? lk = await _lockService.TryAcquireAsync(key, expiry, ct);
/// if (lk is null) return Results.Conflict(...);
/// // critical section — only one instance runs this at a time
/// </code>
/// If the process crashes before Dispose is called, Redis releases the lock automatically
/// when the TTL expires — this is why every lock must have a TTL.
/// </remarks>
public interface IDistributedLock : IAsyncDisposable
{
    /// <summary>The Redis key that was locked (same value passed to TryAcquireAsync).</summary>
    string Resource { get; }
}
