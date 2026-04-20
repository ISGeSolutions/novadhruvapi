using Nova.Shared.Data;

namespace Nova.OpsGroups.Api.Configuration;

/// <summary>
/// Read-only connection to the shared <c>nova_auth</c> database.
/// OpsGroups.Api reads user security rights and team member profiles from here.
/// Stored in <c>appsettings.json → AuthDb</c>.
/// </summary>
public sealed class AuthDbSettings
{
    public const string SectionName = "AuthDb";

    public string ConnectionString { get; set; } = string.Empty;
    public DbType DbType           { get; set; } = DbType.MsSql;
}
