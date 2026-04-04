using Nova.Shared.Data;
using Nova.Shared.Messaging;

namespace Nova.Shared.Tenancy;

/// <summary>Represents a single tenant entry from the <c>Tenants</c> array in appsettings.json.</summary>
public sealed record TenantRecord
{
    /// <summary>Unique tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>Human-readable display name for the tenant.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Database engine used by this tenant.</summary>
    public required DbType DbType { get; init; }

    /// <summary>Encrypted connection string.</summary>
    public required string ConnectionString { get; init; }

    /// <summary>Schema version: <c>legacy</c> or <c>v1</c>.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>
    /// Message broker used by the outbox relay for this tenant.
    /// Defaults to <see cref="BrokerType.RabbitMq"/> if not specified.
    /// Configured in <c>appsettings.json → Tenants[].BrokerType</c>.
    /// </summary>
    public BrokerType BrokerType { get; init; } = BrokerType.RabbitMq;
}
