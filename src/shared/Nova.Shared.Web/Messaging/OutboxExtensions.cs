using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Nova.Shared.Web.Messaging;

/// <summary>Extension methods for wiring the outbox relay into an ASP.NET Core service.</summary>
public static class OutboxExtensions
{
    /// <summary>
    /// Registers the outbox relay background worker and its dependencies.
    /// Call this in <c>Program.cs</c> before <c>builder.Build()</c>.
    /// </summary>
    /// <remarks>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="RabbitMqPublisher"/> — keyed as <c>"RabbitMq"</c></item>
    ///   <item><see cref="RedisStreamPublisher"/> — keyed as <c>"Redis"</c></item>
    ///   <item><see cref="IOutboxRepository"/> / <see cref="OutboxRepository"/></item>
    ///   <item><see cref="OutboxRelayWorker"/> — hosted background service</item>
    /// </list>
    ///
    /// Requires that the following are already registered:
    /// <see cref="Nova.Shared.Security.ICipherService"/>,
    /// <see cref="Nova.Shared.Data.IDbConnectionFactory"/>,
    /// <see cref="Nova.Shared.Locking.IDistributedLockService"/>,
    /// <see cref="StackExchange.Redis.IConnectionMultiplexer"/>.
    /// </remarks>
    public static WebApplicationBuilder AddNovaOutboxRelay(this WebApplicationBuilder builder)
    {
        builder.Services.AddKeyedSingleton<IMessagePublisher, RabbitMqPublisher>("RabbitMq");
        builder.Services.AddKeyedSingleton<IMessagePublisher, RedisStreamPublisher>("Redis");
        builder.Services.AddSingleton<IOutboxRepository, OutboxRepository>();
        builder.Services.AddHostedService<OutboxRelayWorker>();
        return builder;
    }
}
