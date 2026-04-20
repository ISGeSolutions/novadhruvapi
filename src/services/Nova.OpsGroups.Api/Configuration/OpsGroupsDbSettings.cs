using Nova.Shared.Data;

namespace Nova.OpsGroups.Api.Configuration;

public sealed class OpsGroupsDbSettings
{
    public const string SectionName = "OpsGroupsDb";

    public string ConnectionString { get; set; } = string.Empty;
    public DbType DbType           { get; set; } = DbType.MsSql;
}
