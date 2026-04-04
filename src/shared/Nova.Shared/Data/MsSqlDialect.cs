namespace Nova.Shared.Data;

/// <summary>SQL Server-specific dialect implementation.</summary>
public sealed class MsSqlDialect : ISqlDialect
{
    /// <inheritdoc />
    public string TableRef(string databaseOrSchema, string table) =>
        $"{databaseOrSchema}.dbo.{table}";

    /// <inheritdoc />
    public string PaginationClause(int skip, int take) =>
        $"ORDER BY (SELECT NULL) OFFSET {skip} ROWS FETCH NEXT {take} ROWS ONLY";

    /// <inheritdoc />
    public string OffsetFetchClause(int skip, int take) =>
        $"OFFSET {skip} ROWS FETCH NEXT {take} ROWS ONLY";

    /// <inheritdoc />
    public string ReturningIdClause() => "OUTPUT INSERTED.SeqNo";

    /// <inheritdoc />
    public string BooleanLiteral(bool value) => value ? "1" : "0";

    /// <inheritdoc />
    public string ActiveRowsFilter() => "frz_ind = 0";

    /// <inheritdoc />
    public string SoftDeleteClause() => "frz_ind = 1";

    /// <inheritdoc />
    public string ParameterPrefix => "@";
}
