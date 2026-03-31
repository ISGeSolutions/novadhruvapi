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

    /// <summary>Returns the SQL clause used after INSERT to retrieve the generated id.</summary>
    string ReturningIdClause();

    /// <summary>Returns the SQL literal for a boolean value.</summary>
    string BooleanLiteral(bool value);

    /// <summary>The parameter prefix character (always <c>@</c> for both dialects but made explicit).</summary>
    string ParameterPrefix { get; }
}
