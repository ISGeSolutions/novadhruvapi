using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Caching;

namespace Nova.Shared.Web.Caching;

/// <summary>Extension methods for registering the Nova caching stack.</summary>
public static class CachingExtensions
{
    /// <summary>
    /// Registers <see cref="ICacheService"/> backed by Redis.
    /// Requires <c>IConnectionMultiplexer</c> to be registered first — call
    /// <c>builder.AddRedisClient("redis")</c> (Aspire) or register it manually before calling this.
    /// </summary>
    public static IServiceCollection AddNovaCaching(this IServiceCollection services)
    {
        services.AddSingleton<ICacheService, RedisCacheService>();
        return services;
    }
}
