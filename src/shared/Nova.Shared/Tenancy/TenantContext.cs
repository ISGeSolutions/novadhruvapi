using Nova.Shared.Data;

namespace Nova.Shared.Tenancy;

/// <summary>Per-request scoped context carrying tenant identity and database routing information.</summary>
public sealed record TenantContext
{
    /// <summary>The unique tenant identifier, sourced from the JWT <c>tenant_id</c> claim.</summary>
    public required string TenantId { get; init; }

    /// <summary>The encrypted connection string for this tenant's database.</summary>
    public required string ConnectionString { get; init; }

    /// <summary>The database engine used by this tenant.</summary>
    public required DbType DbType { get; init; }

    /// <summary>The schema version for this tenant: <c>legacy</c> or <c>v1</c>.</summary>
    public required string SchemaVersion { get; init; }
}
