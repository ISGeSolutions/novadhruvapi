using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nova.Shared.Caching;
using Nova.Shared.Configuration;
using StackExchange.Redis;

namespace Nova.Shared.Web.Caching;

/// <summary>
/// Redis-backed implementation of <see cref="ICacheService"/>.
/// Uses named <see cref="CacheProfile"/> entries from <c>opsettings.json → Caching.Profiles</c>
/// to determine TTL and whether caching is active for each call site.
/// Falls back to the factory (cache miss) if Redis is unavailable.
/// </summary>
internal sealed class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer  _multiplexer;
    private readonly IOptionsMonitor<OpsSettings> _opsMonitor;
    private readonly ILogger<RedisCacheService>   _logger;

    public RedisCacheService(
        IConnectionMultiplexer       multiplexer,
        IOptionsMonitor<OpsSettings> opsMonitor,
        ILogger<RedisCacheService>   logger)
    {
        _multiplexer = multiplexer;
        _opsMonitor  = opsMonitor;
        _logger      = logger;
    }

    /// <inheritdoc/>
    public async Task<T?> GetOrSetAsync<T>(
        string            key,
        Func<Task<T>>     factory,
        string            profileName,
        CancellationToken cancellationToken = default)
    {
        CacheSettings settings = _opsMonitor.CurrentValue.Caching;

        // Global kill switches — bypass Redis entirely
        if (!settings.GloballyEnabled || settings.EmergencyDisable)
            return await factory();

        // Profile lookup — if profile absent, layer != Redis, or disabled: bypass
        if (!settings.Profiles.TryGetValue(profileName, out CacheProfile? profile)
            || !profile.Enabled
            || !string.Equals(profile.Layer, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            return await factory();
        }

        // DryRunMode — look up the key but always call the factory (observability without serving stale data)
        bool dryRun = settings.DryRunMode;

        // Try cache read
        if (!dryRun)
        {
            T? cached = await TryGetAsync<T>(key);
            if (cached is not null)
                return cached;
        }

        // Cache miss (or dry run) — call the factory
        T? result = await factory();

        if (result is not null)
            await TrySetAsync(key, result, TimeSpan.FromSeconds(profile.TtlSeconds));

        return result;
    }

    /// <inheritdoc/>
    public async Task InvalidateAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            IDatabase db = _multiplexer.GetDatabase();
            await db.KeyDeleteAsync(key);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — cache invalidation skipped for key {CacheKey}", key);
        }
    }

    // -------------------------------------------------------------------------

    private async Task<T?> TryGetAsync<T>(string key)
    {
        try
        {
            IDatabase db    = _multiplexer.GetDatabase();
            RedisValue raw  = await db.StringGetAsync(key);

            if (raw.IsNullOrEmpty)
                return default;

            return JsonSerializer.Deserialize<T>(raw.ToString());
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — cache read skipped for key {CacheKey}", key);
            return default;
        }
    }

    private async Task TrySetAsync<T>(string key, T value, TimeSpan expiry)
    {
        try
        {
            IDatabase db  = _multiplexer.GetDatabase();
            string    json = JsonSerializer.Serialize(value);
            // TimeSpan? is ambiguous in StackExchange.Redis 2.8 (TimeSpan? vs Expiration overloads).
            // Split into two calls: non-nullable TimeSpan converts cleanly to Expiration.
            if (expiry > TimeSpan.Zero)
                await db.StringSetAsync(key, json, expiry);
            else
                await db.StringSetAsync(key, json);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — cache write skipped for key {CacheKey}", key);
        }
    }
}
