using Nova.Shared.Messaging.Outbox;
using StackExchange.Redis;

namespace Nova.Shared.Web.Messaging;

/// <summary>
/// Publishes outbox messages to a Redis Stream.
/// </summary>
/// <remarks>
/// The stream key is derived from the message's <c>Exchange</c> field:
/// <c>nova:events:{Exchange}</c> — e.g. <c>nova:events:bookings</c>.
/// The routing key is stored as the <c>event_type</c> field in the stream entry.
///
/// Consumers should use Redis consumer groups (<c>XREADGROUP</c>) for at-least-once delivery.
/// </remarks>
internal sealed class RedisStreamPublisher : IMessagePublisher
{
    private readonly IConnectionMultiplexer _redis;

    public RedisStreamPublisher(IConnectionMultiplexer redis) => _redis = redis;

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct = default)
    {
        IDatabase db        = _redis.GetDatabase();
        string    streamKey = $"nova:events:{message.Exchange}";

        await db.StreamAddAsync(streamKey,
        [
            new NameValueEntry("message_id",    message.Id),
            new NameValueEntry("event_type",    message.RoutingKey),
            new NameValueEntry("payload",        message.Payload),
            new NameValueEntry("content_type",   message.ContentType),
            new NameValueEntry("correlation_id", message.CorrelationId ?? string.Empty),
        ]);
    }
}
