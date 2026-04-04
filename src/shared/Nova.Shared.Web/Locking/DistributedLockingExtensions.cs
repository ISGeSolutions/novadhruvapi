using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Locking;

namespace Nova.Shared.Web.Locking;

/// <summary>Extension methods for registering the Nova distributed locking stack.</summary>
public static class DistributedLockingExtensions
{
    /// <summary>
    /// Registers <see cref="IDistributedLockService"/> backed by Redis.
    /// Requires <c>IConnectionMultiplexer</c> to be registered first — call
    /// <c>builder.AddRedisClient("redis")</c> before calling this.
    /// </summary>
    public static IServiceCollection AddNovaDistributedLocking(this IServiceCollection services)
    {
        services.AddSingleton<IDistributedLockService, RedisDistributedLockService>();
        return services;
    }
}
