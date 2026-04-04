namespace Nova.Shared.Messaging;

/// <summary>The message broker used for outbox relay for a given tenant.</summary>
public enum BrokerType
{
    /// <summary>RabbitMQ — publish to an exchange with a routing key.</summary>
    RabbitMq,

    /// <summary>Redis Streams — append to a stream using the exchange name as the stream key.</summary>
    Redis,
}
