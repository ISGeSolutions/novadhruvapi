using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.Presets.Api.Configuration;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.Presets.Api.Endpoints;

/// <summary>
/// Tour Generics catalogue: <c>POST /api/v1/groups/tour-generics</c>
/// Tour Generics typeahead: <c>POST /api/v1/groups/tour-generics/search</c>
///
/// Relocated from Nova.OpsGroups.Api. Catalogue returns all active TGs for the
/// tenant (typically a few hundred; caller uses Fuse.js for client-side fuzzy search).
/// Search endpoint is a server-side fallback for per-keystroke typeahead when
/// tenantConfig.search.tgMode === 'like' or catalogue exceeds ~2000 entries.
/// </summary>
public static class TourGenericsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/groups/tour-generics", HandleCatalogueAsync)
             .RequireAuthorization()
             .WithName("TourGenericsCatalogue");

        group.MapPost("/groups/tour-generics/search", HandleSearchAsync)
             .RequireAuthorization()
             .WithName("TourGenericsSearch");
    }

    // -------------------------------------------------------------------------
    // Catalogue — full list, client-side fuzzy search via Fuse.js
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleCatalogueAsync(
        CatalogueRequest             request,
        HttpContext                  httpContext,
        IDbConnectionFactory         connectionFactory,
        IOptions<PresetsDbSettings>  presetsDbOptions,
        CancellationToken            ct)
    {
        Dictionary<string, string[]> errors = RequestContextValidator.Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        string? jwtTenantId = httpContext.User.FindFirstValue("tenant_id");
        if (!string.Equals(request.TenantId, jwtTenantId, StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        PresetsDbSettings presetsDb = presetsDbOptions.Value;
        ISqlDialect       dialect   = PresetsDbHelper.Dialect(presetsDb.DbType);
        string            table     = dialect.TableRef("presets", "tour_generics");
        string            falsy     = dialect.BooleanLiteral(false);

        IEnumerable<TourGenericRow> rows;
        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(
                   presetsDb.ConnectionString, presetsDb.DbType))
        {
            rows = await conn.QueryAsync<TourGenericRow>(
                $"""
                SELECT code AS Code, name AS Name
                FROM   {table}
                WHERE  tenant_id = @TenantId
                AND    frz_ind   = {falsy}
                ORDER  BY name
                """,
                new { request.TenantId },
                commandTimeout: 10);
        }

        return TypedResults.Ok(new
        {
            tour_generics = rows.Select(r => new { code = r.Code, name = r.Name }).ToList(),
        });
    }

    // -------------------------------------------------------------------------
    // Search — server-side LIKE typeahead (fallback for large catalogues)
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleSearchAsync(
        SearchRequest                request,
        HttpContext                  httpContext,
        IDbConnectionFactory         connectionFactory,
        IOptions<PresetsDbSettings>  presetsDbOptions,
        CancellationToken            ct)
    {
        Dictionary<string, string[]> errors = RequestContextValidator.Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        string? jwtTenantId = httpContext.User.FindFirstValue("tenant_id");
        if (!string.Equals(request.TenantId, jwtTenantId, StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        if (string.IsNullOrWhiteSpace(request.Query))
            return TypedResults.Ok(new { tour_generics = Array.Empty<object>() });

        string field = request.Field is "code" or "name" ? request.Field : "name";
        int    limit = Math.Clamp(request.Limit ?? 20, 1, 100);

        PresetsDbSettings presetsDb = presetsDbOptions.Value;
        ISqlDialect       dialect   = PresetsDbHelper.Dialect(presetsDb.DbType);
        string            table     = dialect.TableRef("presets", "tour_generics");
        string            falsy     = dialect.BooleanLiteral(false);
        string            colName   = field == "code" ? "code" : "name";

        string page = dialect.OffsetFetchClause(0, limit);

        IEnumerable<TourGenericRow> rows;
        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(
                   presetsDb.ConnectionString, presetsDb.DbType))
        {
            rows = await conn.QueryAsync<TourGenericRow>(
                $"""
                SELECT code AS Code, name AS Name
                FROM   {table}
                WHERE  tenant_id  = @TenantId
                AND    {colName}  LIKE @Pattern
                AND    frz_ind    = {falsy}
                ORDER  BY {colName}
                {page}
                """,
                new { request.TenantId, Pattern = $"%{request.Query}%" },
                commandTimeout: 10);
        }

        return TypedResults.Ok(new
        {
            tour_generics = rows.Select(r => new { code = r.Code, name = r.Name }).ToList(),
        });
    }

    private sealed record TourGenericRow(string Code, string Name);

    private sealed record CatalogueRequest : RequestContext;

    private sealed record SearchRequest : RequestContext
    {
        public string  Query { get; set; } = string.Empty;
        public string? Field { get; set; }
        public int?    Limit { get; set; }
    }
}
