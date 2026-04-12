using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Nova.CommonUX.Api.Services;

/// <summary>
/// Redis-backed <see cref="ISessionStore"/> for multi-instance deployments.
/// Registered when <c>opsettings.json → Cache.CacheProvider = "Redis"</c>.
/// </summary>
internal sealed class RedisSessionStore : ISessionStore
{
    private readonly IConnectionMultiplexer    _multiplexer;
    private readonly ILogger<RedisSessionStore> _logger;

    public RedisSessionStore(IConnectionMultiplexer multiplexer, ILogger<RedisSessionStore> logger)
    {
        _multiplexer = multiplexer;
        _logger      = logger;
    }

    public async Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken ct = default)
    {
        try
        {
            IDatabase db = _multiplexer.GetDatabase();
            await db.StringSetAsync(key, value, expiry);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error on SET {Key}", key);
            throw;
        }
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            IDatabase  db  = _multiplexer.GetDatabase();
            RedisValue val = await db.StringGetAsync(key);
            return val.IsNullOrEmpty ? null : val.ToString();
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error on GET {Key}", key);
            throw;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            IDatabase db = _multiplexer.GetDatabase();
            await db.KeyDeleteAsync(key);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error on DEL {Key}", key);
            throw;
        }
    }

    public async Task DeleteByPatternAsync(string pattern, CancellationToken ct = default)
    {
        try
        {
            IServer server = _multiplexer.GetServer(_multiplexer.GetEndPoints().First());
            var keys = server.Keys(pattern: pattern).ToArray();
            if (keys.Length == 0) return;

            IDatabase db = _multiplexer.GetDatabase();
            await db.KeyDeleteAsync(keys);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error on pattern DEL {Pattern}", pattern);
            throw;
        }
    }
}
