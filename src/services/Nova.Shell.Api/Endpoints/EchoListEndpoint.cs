using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;

namespace Nova.Shell.Api.Endpoints;

// ---------------------------------------------------------------------------
// REFERENCE ENDPOINT — delete this file when creating a real domain service.
//
// Demonstrates the Nova pagination contract:
//
//  1. Request inherits PagedRequest (which inherits RequestContext).
//     All 7 context fields + page_number + page_size come automatically.
//
//  2. Validation order for paginated endpoints (always in this sequence):
//       a. RequestContextValidator.Validate()   — context fields (400)
//       b. RequestContextValidator.TenantMatches() — tenant check (403)
//       c. PagedRequestValidator.Validate()     — page_number / page_size (400)
//       d. Domain-specific field validation     — business rules (400/422)
//
//  3. SQL pattern (real implementation):
//       int totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);
//       IEnumerable<T> rows = await connection.QueryAsync<T>(dataSql, parameters);
//       return TypedResults.Ok(PagedResult<T>.From(rows, totalCount, request.PageNumber, request.PageSize));
//
//  4. ISqlDialect.PaginationClause uses request.Skip and request.PageSize.
//       string pagination = dialect.PaginationClause(request.Skip, request.PageSize);
//       → MSSQL:   "ORDER BY (SELECT NULL) OFFSET {skip} ROWS FETCH NEXT {take} ROWS ONLY"
//       → Postgres: "LIMIT {take} OFFSET {skip}"
//
//  5. Soft-delete convention — all data queries must filter on frz_ind:
//       string activeFilter = dialect.ActiveRowsFilter();
//       → MSSQL/MariaDB: "frz_ind = 0"
//       → Postgres:      "frz_ind = false"
//
//     To soft-delete a row (never hard-delete):
//       string softDelete = dialect.SoftDeleteClause();
//       → MSSQL/MariaDB: "frz_ind = 1"
//       → Postgres:      "frz_ind = true"
//       e.g. $"UPDATE {tableRef} SET {softDelete} WHERE id = {dialect.ParameterPrefix}id"
//
//  ISqlDialect is injected as a scoped service — resolved automatically for the current
//  tenant's DB engine by AddNovaTenancy(). No manual new MsSqlDialect() needed.
// ---------------------------------------------------------------------------

/// <summary>
/// Reference endpoint: <c>POST /echo/list</c>.
/// Shows <see cref="PagedRequest"/> inheritance, <see cref="PagedRequestValidator"/>,
/// and <see cref="PagedResult{T}"/> response shape.
/// </summary>
public static class EchoListEndpoint
{
    // Simulated total — a real endpoint gets this from a COUNT query.
    private const int SimulatedTotalCount = 47;

    /// <summary>Registers the endpoint on the versioned route group.</summary>
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/echo/list", Handle)
             .RequireAuthorization()   // TenantContext requires a valid JWT — send Bearer {{access_token}}
             .WithName("EchoList");
    }

    private static IResult Handle(
        EchoListRequest request,
        TenantContext tenantContext,
        IDbConnectionFactory connectionFactory)
    {
        // Step 1 — standard context fields (400 if any required field is missing)
        Dictionary<string, string[]> contextErrors = RequestContextValidator.Validate(request);
        if (contextErrors.Count > 0)
            return TypedResults.ValidationProblem(contextErrors, title: "Validation failed");

        // Step 2 — tenant mismatch (403)
        if (!RequestContextValidator.TenantMatches(request, tenantContext))
        {
            return TypedResults.Problem(
                title: "Forbidden",
                detail: "tenant_id in the request body does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Step 3 — pagination field validation (400 if page_number < 1 or page_size out of range)
        Dictionary<string, string[]> pageErrors = PagedRequestValidator.Validate(request);
        if (pageErrors.Count > 0)
            return TypedResults.ValidationProblem(pageErrors, title: "Validation failed");

        // Step 4 — domain field validation (add your own checks here)
        // e.g. if (request.FromDate > request.ToDate) return ...

        // -----------------------------------------------------------------------
        // Real implementation pattern (replace the simulation below):
        //
        //   string tableRef   = dialect.TableRef("bookings", "booking");
        //   string pagination = dialect.PaginationClause(request.Skip, request.PageSize);
        //   string active     = dialect.ActiveRowsFilter();   // "frz_ind = 0" / "frz_ind = false"
        //
        //   string countSql = $"SELECT COUNT(*) FROM {tableRef} WHERE {active}";
        //   string dataSql  = $"SELECT id, reference FROM {tableRef} WHERE {active} {pagination}";
        //
        //   using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);
        //   int totalCount = await connection.ExecuteScalarAsync<int>(countSql);
        //   IEnumerable<BookingSummary> rows = await connection.QueryAsync<BookingSummary>(dataSql);
        //
        //   return TypedResults.Ok(PagedResult<BookingSummary>.From(rows, totalCount, request.PageNumber, request.PageSize));
        // -----------------------------------------------------------------------

        // Simulated data — returns a realistic page from a pretend 47-item dataset.
        int available = Math.Max(0, SimulatedTotalCount - request.Skip);
        int count     = Math.Min(request.PageSize, available);

        IEnumerable<EchoItem> items = Enumerable
            .Range(request.Skip + 1, count)
            .Select(i => new EchoItem(
                Id:    $"item-{i:D3}",
                Label: $"Echo item {i} (tenant: {request.TenantId})"));

        return TypedResults.Ok(
            PagedResult<EchoItem>.From(items, SimulatedTotalCount, request.PageNumber, request.PageSize));
    }

    // ---------------------------------------------------------------------------
    // Request — inherits all 9 standard fields (7 context + page_number + page_size).
    // Wire format includes:
    //   "tenant_id", "company_code", "branch_code", "user_id",
    //   "browser_locale", "browser_timezone", "ip_address",
    //   "page_number", "page_size",
    //   "filter"  ← domain-specific optional filter
    // ---------------------------------------------------------------------------
    private sealed record EchoListRequest : PagedRequest
    {
        /// <summary>Optional free-text filter. Mapped to a SQL LIKE in a real implementation.</summary>
        public string? Filter { get; init; }
    }

    // Response item — wire format: { "id": "item-001", "label": "Echo item 1 ..." }
    private sealed record EchoItem(string Id, string Label);
}
