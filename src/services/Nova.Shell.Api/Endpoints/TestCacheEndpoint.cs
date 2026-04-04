using Nova.Shared.Caching;

namespace Nova.Shell.Api.Endpoints;

/// <summary>
/// Maps diagnostic cache endpoints:
/// <list type="bullet">
///   <item><c>GET  /test-cache</c> — GetOrSetAsync reference: returns a cached timestamp. First call
///         stores it; subsequent calls return the same value until the TTL expires or it is invalidated.</item>
///   <item><c>DELETE /test-cache</c> — InvalidateAsync reference: removes the cached value.</item>
/// </list>
/// </summary>
public static class TestCacheEndpoint
{
    private const string CacheKey     = "nova:test:hello";
    private const string CacheProfile = "ReferenceData";   // maps to opsettings.json → Caching.Profiles

    /// <summary>Registers the endpoints on the given <see cref="WebApplication"/>.</summary>
    public static void Map(WebApplication app)
    {
        // GET /test-cache
        // Demonstrates GetOrSetAsync: first call runs the factory and caches the result.
        // Subsequent calls return the cached value — the cached_at timestamp stays the same
        // until the key expires (profile TTL) or DELETE /test-cache is called.
        app.MapGet("/test-cache", async (ICacheService cache) =>
        {
            CachePayload result = (await cache.GetOrSetAsync<CachePayload>(
                key:         CacheKey,
                factory:     () => Task.FromResult(new CachePayload(DateTimeOffset.UtcNow)),
                profileName: CacheProfile))!;

            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("TestCacheGet");

        // DELETE /test-cache
        // Demonstrates InvalidateAsync: removes the key so the next GET runs the factory again.
        app.MapDelete("/test-cache", async (ICacheService cache) =>
        {
            await cache.InvalidateAsync(CacheKey);
            return Results.Ok(new { invalidated = CacheKey });
        })
        .AllowAnonymous()
        .WithName("TestCacheInvalidate");
    }

    // CACHE REFERENCE — how to use ICacheService in a real endpoint:
    //
    // 1. Choose a profile name from opsettings.json → Caching.Profiles:
    //      "ReferenceData"   — Redis, 1 hour TTL, suitable for lookup/config data
    //      "TransactionData" — None (disabled), for data that must never be stale
    //
    // 2. Choose a key that uniquely identifies the data:
    //      Tenant-scoped:  "tenant:{tenantId}:{entity}:{id}"   e.g. "tenant:BLDK:booking:BK-001"
    //      Global shared:  "nova:{service}:{entity}"           e.g. "nova:shell:config:v1"
    //
    // 3. Call GetOrSetAsync — the factory is only invoked on a cache miss:
    //
    //      BookingSummary? summary = await _cache.GetOrSetAsync<BookingSummary>(
    //          key:         $"tenant:{tenantId}:booking:{bookingRef}",
    //          factory:     () => _repo.GetBookingAsync(bookingRef, ct),
    //          profileName: "ReferenceData",
    //          cancellationToken: ct);
    //
    // 4. Invalidate after a write:
    //
    //      await _cache.InvalidateAsync($"tenant:{tenantId}:booking:{bookingRef}");
    //
    // Resilience: if Redis is unavailable, the factory is always called (treated as a cache miss).
    // The application must be able to operate correctly without the cache at all times.

    private sealed record CachePayload(DateTimeOffset CachedAt);
}
