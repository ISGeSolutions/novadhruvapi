using Microsoft.Extensions.Options;
using Nova.Shared.Configuration;

namespace Nova.Shared.Tenancy;

/// <summary>
/// Loads tenant records from configuration and provides lookup by tenant id.
/// Connection strings are stored encrypted; decryption occurs in the infrastructure layer.
/// </summary>
public sealed class TenantRegistry
{
    private readonly Dictionary<string, TenantRecord> _tenants;

    /// <summary>Initialises the registry from the application settings.</summary>
    public TenantRegistry(IOptions<AppSettings> options)
    {
        _tenants = options.Value.Tenants
            .ToDictionary(t => t.TenantId, t => t, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Returns the total number of registered tenants.</summary>
    public int Count => _tenants.Count;

    /// <summary>Attempts to find a tenant record by id.</summary>
    public bool TryGetTenant(string tenantId, out TenantRecord? record) =>
        _tenants.TryGetValue(tenantId, out record);

    /// <summary>Returns all registered tenant records.</summary>
    public IReadOnlyCollection<TenantRecord> All => _tenants.Values;
}
