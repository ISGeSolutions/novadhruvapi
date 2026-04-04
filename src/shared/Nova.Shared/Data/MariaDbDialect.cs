namespace Nova.Shared.Data;

/// <summary>MariaDB (MySQL) specific dialect implementation.</summary>
public sealed class MariaDbDialect : ISqlDialect
{
    /// <inheritdoc />
    public string TableRef(string databaseOrSchema, string table) =>
        $"`{databaseOrSchema}`.`{table}`";

    /// <inheritdoc />
    public string PaginationClause(int skip, int take) =>
        $"LIMIT {take} OFFSET {skip}";

    /// <inheritdoc />
    public string OffsetFetchClause(int skip, int take) =>
        $"LIMIT {take} OFFSET {skip}";

    /// <inheritdoc />
    public string ReturningIdClause() => "; SELECT LAST_INSERT_ID();";

    /// <inheritdoc />
    public string BooleanLiteral(bool value) => value ? "1" : "0";

    /// <inheritdoc />
    public string ActiveRowsFilter() => "frz_ind = 0";

    /// <inheritdoc />
    public string SoftDeleteClause() => "frz_ind = 1";

    /// <inheritdoc />
    public string ParameterPrefix => "@";
}
