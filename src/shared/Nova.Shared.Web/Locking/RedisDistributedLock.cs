using Microsoft.Extensions.Logging;
using Nova.Shared.Locking;
using StackExchange.Redis;

namespace Nova.Shared.Web.Locking;

/// <summary>
/// Holds an acquired Redis lock and releases it atomically via a Lua script on dispose.
/// </summary>
/// <remarks>
/// <para><b>Why a Lua script for release?</b></para>
/// Releasing a lock requires two steps: check that the key still holds our token, then delete
/// it. Without atomicity, a race can occur:
/// <list type="number">
///   <item>Our TTL expires mid-operation — Redis removes the key.</item>
///   <item>Another instance acquires the lock — the key now holds their token.</item>
///   <item>We run DEL — we delete their lock, not ours.</item>
/// </list>
/// Redis executes Lua scripts as a single atomic unit. No other command from any client can
/// run between the GET and the DEL inside the script. This makes the release safe.
///
/// <para>The script:</para>
/// <code>
/// if redis.call("GET", KEYS[1]) == ARGV[1] then
///     return redis.call("DEL", KEYS[1])
/// else
///     return 0    -- key was already gone or belongs to another instance
/// end
/// </code>
///
/// <para><b>What happens if Redis is unavailable at release time?</b></para>
/// A warning is logged. The lock will be released automatically by Redis when its TTL expires —
/// this is why every lock must be created with an appropriate TTL. The worst case is a brief
/// period where the resource appears locked when it is not, bounded by the TTL.
/// </remarks>
internal sealed class RedisDistributedLock : IDistributedLock
{
    // Prepared once — Redis caches the script by SHA so only the hash is sent on subsequent calls.
    private static readonly LuaScript ReleaseScript = LuaScript.Prepare("""
        if redis.call("GET", @key) == @token then
            return redis.call("DEL", @key)
        else
            return 0
        end
        """);

    private readonly IDatabase              _db;
    private readonly string                 _token;
    private readonly ILogger                _logger;
    private          bool                   _released;

    /// <inheritdoc/>
    public string Resource { get; }

    internal RedisDistributedLock(IDatabase db, string resource, string token, ILogger logger)
    {
        _db       = db;
        Resource  = resource;
        _token    = token;
        _logger   = logger;
    }

    /// <summary>
    /// Releases the lock. Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_released) return;
        _released = true;

        try
        {
            // Pass key and token as named parameters — LuaScript substitutes @key and @token.
            await _db.ScriptEvaluateAsync(ReleaseScript, new { key = (RedisKey)Resource, token = (RedisValue)_token });
            _logger.LogDebug("Distributed lock released: {LockResource}", Resource);
        }
        catch (RedisException ex)
        {
            // Redis unavailable at release time — TTL will expire the lock automatically.
            _logger.LogWarning(ex,
                "Redis unavailable — lock release skipped for {LockResource}. TTL will expire it automatically.",
                Resource);
        }
    }
}
