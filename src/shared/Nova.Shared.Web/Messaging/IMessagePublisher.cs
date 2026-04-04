using Nova.Shared.Messaging.Outbox;

namespace Nova.Shared.Web.Messaging;

/// <summary>Publishes a single outbox message to a message broker.</summary>
public interface IMessagePublisher
{
    /// <summary>
    /// Publishes <paramref name="message"/> to the underlying broker.
    /// Throws on failure — the relay worker handles retry logic.
    /// </summary>
    Task PublishAsync(OutboxMessage message, CancellationToken ct = default);
}
