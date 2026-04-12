namespace Nova.Shared.Locking;

/// <summary>
/// Acquires short-lived exclusive locks in Redis so that only one application instance
/// executes a critical section at a time.
/// </summary>
/// <remarks>
/// <para><b>When to use:</b></para>
/// <list type="bullet">
///   <item>Preventing double-booking — two users booking the same seat/room simultaneously.</item>
///   <item>Payment idempotency — ensuring a payment is submitted to the gateway exactly once.</item>
///   <item>Inventory reservation — check-then-reserve must be atomic across instances.</item>
///   <item>Background jobs — ensuring a scheduled task runs on exactly one instance.</item>
/// </list>
///
/// <para><b>When NOT to use:</b></para>
/// <list type="bullet">
///   <item>Instead of a database unique constraint — use both; the lock prevents the race,
///         the DB constraint is the safety net.</item>
///   <item>For long-running operations (&gt; 60 s) — the TTL may expire before the work finishes.</item>
///   <item>For read-only operations — no lock needed; reads do not mutate state.</item>
/// </list>
///
/// <para><b>Key naming convention:</b></para>
/// <list type="bullet">
///   <item>Tenant-scoped:  <c>tenant:{tenantId}:{entity}:{id}</c>
///         e.g. <c>tenant:BTDK:booking:create:BK-001</c></item>
///   <item>Global job:     <c>nova:job:{job-name}</c>
///         e.g. <c>nova:job:send-reminders</c></item>
/// </list>
///
/// <para><b>TTL guidance:</b></para>
/// <list type="bullet">
///   <item>DB read + write: 15–30 s</item>
///   <item>Payment gateway call: 30–60 s</item>
///   <item>Background job: expected duration + 20 % buffer</item>
/// </list>
///
/// <para><b>If Redis is unavailable</b>, <see cref="TryAcquireAsync"/> returns <c>null</c>.
/// Treat this the same as "lock held" — do not proceed with the critical section.</para>
/// </remarks>
public interface IDistributedLockService
{
    /// <summary>
    /// Attempts to acquire an exclusive lock on <paramref name="resource"/>.
    /// </summary>
    /// <param name="resource">
    ///   The Redis key to lock. Must uniquely identify the resource being protected.
    ///   Follow the key naming convention in the remarks.
    /// </param>
    /// <param name="expiry">
    ///   How long the lock is held in Redis. If the process crashes before the lock is
    ///   released, Redis removes it automatically after this duration.
    ///   Choose a value longer than the expected operation time.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///   A held <see cref="IDistributedLock"/> if the lock was acquired, or <c>null</c>
    ///   if the resource is already locked or Redis is unavailable.
    ///   Dispose the returned lock (via <c>await using</c>) to release it.
    /// </returns>
    Task<IDistributedLock?> TryAcquireAsync(
        string            resource,
        TimeSpan          expiry,
        CancellationToken ct = default);
}
