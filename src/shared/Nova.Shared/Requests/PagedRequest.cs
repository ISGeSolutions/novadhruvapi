namespace Nova.Shared.Requests;

/// <summary>
/// Base record for all paginated POST requests.
/// Inherits the seven standard context fields from <see cref="RequestContext"/>
/// and adds <see cref="PageNumber"/> and <see cref="PageSize"/>.
/// </summary>
/// <remarks>
/// Domain request records that return paginated results should inherit from this:
/// <code>
/// private sealed record SearchBookingsRequest : PagedRequest
/// {
///     public DateOnly FromDate { get; init; }
///     public DateOnly ToDate { get; init; }
/// }
/// </code>
///
/// Use <see cref="Skip"/> with <c>ISqlDialect.PaginationClause(request.Skip, request.PageSize)</c>
/// to build the pagination fragment for Dapper queries.
/// </remarks>
public record PagedRequest : RequestContext
{
    /// <summary>
    /// The 1-based page number to retrieve. Defaults to <c>1</c>.
    /// Wire format: <c>page_number</c>.
    /// </summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>
    /// The number of items per page. Defaults to <c>25</c>.
    /// Maximum enforced by <c>PagedRequestValidator</c>: <c>100</c>.
    /// Wire format: <c>page_size</c>.
    /// </summary>
    public int PageSize { get; init; } = 25;

    /// <summary>
    /// The number of rows to skip for this page.
    /// Use directly as the <c>skip</c> argument to <c>ISqlDialect.PaginationClause</c>.
    /// </summary>
    public int Skip => (PageNumber - 1) * PageSize;
}
