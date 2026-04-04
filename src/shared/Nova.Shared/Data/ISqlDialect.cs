namespace Nova.Shared.Data;

/// <summary>
/// Provides database-engine-specific SQL fragments and conventions.
/// </summary>
public interface ISqlDialect
{
    /// <summary>Builds a fully-qualified table reference appropriate for the dialect.</summary>
    string TableRef(string databaseOrSchema, string table);

    /// <summary>Returns a SQL OFFSET/FETCH or LIMIT/OFFSET pagination clause.</summary>
    string PaginationClause(int skip, int take);

    /// <summary>
    /// Returns only the offset/fetch fragment — no ORDER BY prefix.
    /// Use this on queries that already carry an explicit ORDER BY clause.
    /// <list type="bullet">
    ///   <item>MSSQL:         <c>OFFSET {skip} ROWS FETCH NEXT {take} ROWS ONLY</c></item>
    ///   <item>Postgres/MariaDB: <c>LIMIT {take} OFFSET {skip}</c></item>
    /// </list>
    /// </summary>
    string OffsetFetchClause(int skip, int take);

    /// <summary>Returns the SQL clause used after INSERT to retrieve the generated id.</summary>
    string ReturningIdClause();

    /// <summary>Returns the SQL literal for a boolean value.</summary>
    string BooleanLiteral(bool value);

    /// <summary>
    /// Returns the WHERE predicate fragment that filters to active (non-deleted) rows.
    /// Uses the <c>frz_ind</c> soft-delete column convention: <c>frz_ind = 0</c> (MSSQL/MariaDB)
    /// or <c>frz_ind = false</c> (Postgres).
    /// </summary>
    /// <example>
    /// <code>string sql = $"SELECT * FROM {dialect.TableRef(schema, table)} WHERE {dialect.ActiveRowsFilter()}";</code>
    /// </example>
    string ActiveRowsFilter();

    /// <summary>
    /// Returns the SET assignment fragment used in an UPDATE to soft-delete a row.
    /// Uses the <c>frz_ind</c> column convention: <c>frz_ind = 1</c> (MSSQL/MariaDB)
    /// or <c>frz_ind = true</c> (Postgres).
    /// </summary>
    /// <example>
    /// <code>string sql = $"UPDATE {dialect.TableRef(schema, table)} SET {dialect.SoftDeleteClause()} WHERE id = {dialect.ParameterPrefix}id";</code>
    /// </example>
    string SoftDeleteClause();

    /// <summary>The parameter prefix character (always <c>@</c> for both dialects but made explicit).</summary>
    string ParameterPrefix { get; }
}
