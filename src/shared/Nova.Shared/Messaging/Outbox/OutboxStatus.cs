namespace Nova.Shared.Messaging.Outbox;

/// <summary>Status values stored in the <c>nova_outbox.status</c> column.</summary>
public static class OutboxStatus
{
    /// <summary>Waiting to be picked up by the relay.</summary>
    public const string Pending    = "pending";

    /// <summary>Claimed by the relay — in-flight.</summary>
    public const string Processing = "processing";

    /// <summary>Successfully published to the broker.</summary>
    public const string Sent       = "sent";

    /// <summary>Permanently failed — retry limit reached.</summary>
    public const string Failed     = "failed";
}
