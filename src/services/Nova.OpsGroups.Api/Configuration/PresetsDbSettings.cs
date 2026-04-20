using Nova.Shared.Data;

namespace Nova.OpsGroups.Api.Configuration;

public sealed class PresetsDbSettings
{
    public const string SectionName = "PresetsDb";

    public string ConnectionString { get; set; } = string.Empty;
    public DbType DbType           { get; set; } = DbType.MsSql;
}
