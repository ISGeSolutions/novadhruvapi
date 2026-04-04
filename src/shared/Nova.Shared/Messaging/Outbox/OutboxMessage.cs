namespace Nova.Shared.Messaging.Outbox;

/// <summary>
/// Represents a single record in the <c>outbox_messages</c> table.
/// The relay implementation is not part of the shell — this record is structural only.
/// </summary>
public sealed record OutboxMessage
{
    /// <summary>UUID v7 identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Owning tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>When the message was created (UTC).</summary>
    public required DateTimeOffset CreatedOn { get; init; }

    /// <summary>When the message is scheduled for delivery (UTC), if applicable.</summary>
    public DateTimeOffset? ScheduledOn { get; init; }

    /// <summary>When the message was successfully processed (UTC).</summary>
    public DateTimeOffset? ProcessedOn { get; init; }

    /// <summary>Target exchange name.</summary>
    public required string Exchange { get; init; }

    /// <summary>Message routing key.</summary>
    public required string RoutingKey { get; init; }

    /// <summary>JSON-serialised message payload.</summary>
    public required string Payload { get; init; }

    /// <summary>MIME content type of the payload (e.g. <c>application/json</c>).</summary>
    public required string ContentType { get; init; }

    /// <summary>Number of delivery attempts made so far.</summary>
    public required int RetryCount { get; init; }

    /// <summary>Maximum number of delivery attempts allowed.</summary>
    public required int MaxRetries { get; init; }

    /// <summary>Error detail from the last failed delivery attempt.</summary>
    public string? LastError { get; init; }

    /// <summary>Message status: <c>pending</c>, <c>processing</c>, <c>sent</c>, or <c>failed</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Correlation identifier for distributed tracing.</summary>
    public string? CorrelationId { get; init; }
}
