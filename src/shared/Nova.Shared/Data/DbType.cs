namespace Nova.Shared.Data;

/// <summary>Identifies the target database engine for a tenant.</summary>
public enum DbType
{
    /// <summary>Microsoft SQL Server.</summary>
    MsSql,

    /// <summary>PostgreSQL.</summary>
    Postgres,

    /// <summary>MariaDB / MySQL.</summary>
    MariaDb
}
