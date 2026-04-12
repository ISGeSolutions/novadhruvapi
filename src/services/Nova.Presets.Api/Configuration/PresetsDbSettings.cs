using Nova.Shared.Data;

namespace Nova.Presets.Api.Configuration;

/// <summary>
/// Connection to the <c>presets</c> database — owned by Nova.Presets.Api.
/// Contains legacy Company/Branch tables (MSSQL) and new Nova tables
/// (tenant_user_status, tenant_password_change_requests).
/// Stored in <c>appsettings.json → PresetsDb</c>.
/// </summary>
public sealed class PresetsDbSettings
{
    public const string SectionName = "PresetsDb";

    public string ConnectionString { get; set; } = string.Empty;
    public DbType DbType           { get; set; } = DbType.MsSql;
}
