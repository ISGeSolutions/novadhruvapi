using Nova.Shared.Locking;

namespace Nova.Shell.Api.Endpoints;

/// <summary>
/// Maps diagnostic lock endpoints used to verify and demonstrate <see cref="IDistributedLockService"/>.
/// <list type="bullet">
///   <item><c>GET /test-lock</c> — acquires and immediately releases a lock (normal workflow).</item>
///   <item><c>GET /test-lock?hold=N</c> — acquires and holds for N seconds before releasing.
///         Use to test contention: call with hold=10, then call without hold from a second client.</item>
/// </list>
/// </summary>
public static class TestLockEndpoint
{
    private const string LockKey = "nova:test:lock";

    /// <summary>Registers the endpoints on the given <see cref="WebApplication"/>.</summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/test-lock", async (
            IDistributedLockService lockService,
            int?                    hold,
            CancellationToken       ct) =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // TryAcquireAsync returns null if the lock is already held or Redis is unavailable.
            // Always check for null before entering the critical section.
            await using IDistributedLock? lk = await lockService.TryAcquireAsync(
                resource: LockKey,
                expiry:   TimeSpan.FromSeconds(30),
                ct:       ct);

            if (lk is null)
            {
                return Results.Conflict(new
                {
                    acquired   = false,
                    resource   = LockKey,
                    reason     = "Lock is held by another instance, or Redis is unavailable.",
                    hint       = "Call GET /test-lock?hold=10 first, then immediately call GET /test-lock from a second client to see this response."
                });
            }

            // ── Critical section ────────────────────────────────────────────────
            // Only one instance reaches here at a time.
            // In a real endpoint this is where you would do the check-then-write
            // (e.g. check seat availability, insert booking, update inventory).

            if (hold is > 0)
            {
                // Holding the lock intentionally — lets you test contention from a second client.
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(hold.Value, 30)), ct);
            }
            // ── End of critical section ─────────────────────────────────────────

            stopwatch.Stop();

            return Results.Ok(new
            {
                acquired      = true,
                resource      = lk.Resource,
                held_for_ms   = stopwatch.ElapsedMilliseconds,
                held_for_seconds = hold ?? 0
            });

            // await using disposes lk here — releases the lock via Lua script
        })
        .AllowAnonymous()
        .WithName("TestLock");
    }

    // DISTRIBUTED LOCK REFERENCE — how to use IDistributedLockService in a real endpoint
    //
    // ── Step 1: choose a key that uniquely identifies the protected resource ────────────
    //
    //   Tenant-scoped operation:   "tenant:{tenantId}:{entity}:{id}"
    //   e.g.                       "tenant:BLDK:booking:create:BK-001"
    //   e.g.                       "tenant:BLDK:payment:PAY-999"
    //
    //   Global background job:     "nova:job:{job-name}"
    //   e.g.                       "nova:job:send-reminders"
    //
    // ── Step 2: choose a TTL longer than your expected operation time ───────────────────
    //
    //   DB read + write:           15–30 s
    //   Payment gateway call:      30–60 s
    //   Background job:            expected duration + 20 % buffer
    //
    // ── Step 3: TryAcquireAsync in an await using block ────────────────────────────────
    //
    //   await using IDistributedLock? lk = await _lockService.TryAcquireAsync(
    //       resource: $"tenant:{tenantId}:booking:create:{request.BookingRef}",
    //       expiry:   TimeSpan.FromSeconds(30),
    //       ct:       ct);
    //
    //   if (lk is null)
    //       return Results.Conflict("This booking is already being processed. Try again shortly.");
    //
    //   // critical section — only one instance reaches here at a time
    //   bool alreadyExists = await _repo.BookingExistsAsync(request.BookingRef, ct);
    //   if (alreadyExists)
    //       return Results.Conflict("Booking reference already exists.");
    //
    //   await _repo.CreateBookingAsync(request, ct);
    //   // lock released automatically here by await using
    //
    // ── Always pair with a DB unique constraint ─────────────────────────────────────────
    //
    //   The lock prevents the race condition.
    //   The DB constraint is the safety net for any edge case the lock does not cover
    //   (e.g. lock TTL expired before the write completed).
    //   Both must be in place for correct behaviour.
}
