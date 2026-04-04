namespace Nova.Shared.Requests;

/// <summary>
/// Standard paginated response envelope returned by all list/search endpoints.
/// </summary>
/// <typeparam name="T">The item type for this page.</typeparam>
/// <remarks>
/// Wire format (snake_case):
/// <code>
/// {
///   "items":             [...],
///   "total_count":       47,
///   "page_number":       2,
///   "page_size":         10,
///   "total_pages":       5,
///   "has_next_page":     true,
///   "has_previous_page": true
/// }
/// </code>
///
/// Use the <see cref="From"/> factory to construct from a Dapper query result:
/// <code>
/// int totalCount = await connection.ExecuteScalarAsync&lt;int&gt;(countSql, parameters);
/// IEnumerable&lt;BookingSummary&gt; rows = await connection.QueryAsync&lt;BookingSummary&gt;(dataSql, parameters);
/// return TypedResults.Ok(PagedResult&lt;BookingSummary&gt;.From(rows, totalCount, request.PageNumber, request.PageSize));
/// </code>
/// </remarks>
public sealed record PagedResult<T>
{
    /// <summary>The items for the current page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Total number of records matching the query across all pages.</summary>
    public required int TotalCount { get; init; }

    /// <summary>The 1-based page number returned.</summary>
    public required int PageNumber { get; init; }

    /// <summary>The page size used for this query.</summary>
    public required int PageSize { get; init; }

    /// <summary>Total number of pages. Computed from <see cref="TotalCount"/> and <see cref="PageSize"/>.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary><c>true</c> when a subsequent page exists.</summary>
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary><c>true</c> when a previous page exists.</summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>
    /// Creates a <see cref="PagedResult{T}"/> from a query result set.
    /// </summary>
    /// <param name="items">The items returned by the data query for this page.</param>
    /// <param name="totalCount">Total matching records (from a separate COUNT query).</param>
    /// <param name="pageNumber">The requested page number.</param>
    /// <param name="pageSize">The requested page size.</param>
    public static PagedResult<T> From(
        IEnumerable<T> items,
        int totalCount,
        int pageNumber,
        int pageSize) =>
        new()
        {
            Items      = items.ToList().AsReadOnly(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize   = pageSize
        };
}
