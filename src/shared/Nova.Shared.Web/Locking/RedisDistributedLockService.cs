using Microsoft.Extensions.Logging;
using Nova.Shared.Locking;
using StackExchange.Redis;

namespace Nova.Shared.Web.Locking;

/// <summary>
/// Redis-backed implementation of <see cref="IDistributedLockService"/>.
/// Uses the Redis <c>SET key token NX PX ttl</c> pattern:
/// <list type="bullet">
///   <item><c>NX</c> — only set if the key does Not eXist (atomic check-and-set).</item>
///   <item><c>PX</c> — set expiry in milliseconds (TTL).</item>
///   <item><c>token</c> — a unique value per acquisition so only the holder can release.</item>
/// </list>
/// </summary>
internal sealed class RedisDistributedLockService : IDistributedLockService
{
    private readonly IConnectionMultiplexer            _multiplexer;
    private readonly ILogger<RedisDistributedLockService> _logger;

    public RedisDistributedLockService(
        IConnectionMultiplexer               multiplexer,
        ILogger<RedisDistributedLockService> logger)
    {
        _multiplexer = multiplexer;
        _logger      = logger;
    }

    /// <inheritdoc/>
    public async Task<IDistributedLock?> TryAcquireAsync(
        string            resource,
        TimeSpan          expiry,
        CancellationToken ct = default)
    {
        try
        {
            IDatabase db = _multiplexer.GetDatabase();

            // Each acquisition gets a unique token (UUID without hyphens).
            // This token is stored as the Redis key value and compared at release time
            // by the Lua script — ensures only the holder can delete the key.
            string token = Guid.NewGuid().ToString("N");

            // SET resource token NX PX {expiry_ms}
            // When.NotExists makes this atomic: the key is created only if it does not exist.
            // If it already exists (another instance holds the lock), Redis returns false.
            bool acquired = await db.StringSetAsync(resource, token, expiry, When.NotExists);

            if (!acquired)
            {
                _logger.LogDebug(
                    "Distributed lock not acquired — already held: {LockResource}", resource);
                return null;
            }

            _logger.LogDebug(
                "Distributed lock acquired: {LockResource} (TTL: {ExpirySeconds}s)",
                resource, (int)expiry.TotalSeconds);

            return new RedisDistributedLock(db, resource, token, _logger);
        }
        catch (RedisException ex)
        {
            // Redis unavailable — treat as "could not acquire".
            // Do NOT proceed with the critical section when the lock cannot be confirmed.
            _logger.LogWarning(ex,
                "Redis unavailable — could not acquire distributed lock for {LockResource}",
                resource);
            return null;
        }
    }
}
