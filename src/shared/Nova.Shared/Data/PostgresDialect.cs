namespace Nova.Shared.Data;

/// <summary>PostgreSQL-specific dialect implementation.</summary>
public sealed class PostgresDialect : ISqlDialect
{
    /// <inheritdoc />
    public string TableRef(string databaseOrSchema, string table) =>
        $"{databaseOrSchema}.{table}";

    /// <inheritdoc />
    public string PaginationClause(int skip, int take) =>
        $"LIMIT {take} OFFSET {skip}";

    /// <inheritdoc />
    public string ReturningIdClause() => "RETURNING id;";

    /// <inheritdoc />
    public string BooleanLiteral(bool value) => value ? "true" : "false";

    /// <inheritdoc />
    public string ActiveRowsFilter() => "frz_ind = false";

    /// <inheritdoc />
    public string SoftDeleteClause() => "frz_ind = true";

    /// <inheritdoc />
    public string ParameterPrefix => "@";
}
