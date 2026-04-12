using Nova.Shared.Data;

namespace Nova.Presets.Api.Configuration;

/// <summary>
/// Read-only connection to the shared <c>nova_auth</c> database.
/// Presets.Api reads user profile and auth data from here; password hash writes
/// go here on change-password confirmation.
/// Stored in <c>appsettings.json → AuthDb</c>.
/// </summary>
public sealed class AuthDbSettings
{
    public const string SectionName = "AuthDb";

    public string ConnectionString { get; set; } = string.Empty;
    public DbType DbType           { get; set; } = DbType.MsSql;
}
