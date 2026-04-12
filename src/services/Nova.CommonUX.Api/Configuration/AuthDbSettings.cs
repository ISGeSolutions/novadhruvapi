using Nova.Shared.Data;

namespace Nova.CommonUX.Api.Configuration;

/// <summary>
/// Connection settings for the <c>nova_auth</c> database — the single shared auth database
/// owned exclusively by Nova.CommonUX.Api. Stored in <c>appsettings.json → AuthDb</c>.
/// </summary>
public sealed class AuthDbSettings
{
    public const string SectionName = "AuthDb";

    /// <summary>Encrypted connection string (CipherService-encrypted).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Database engine. Determines the SQL dialect used at runtime.</summary>
    public DbType DbType { get; set; } = DbType.MsSql;
}
