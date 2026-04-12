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
/// Branches: <c>POST /api/v1/branches</c>
///
/// Returns all active branches for the tenant across all companies.
/// MSSQL: queries legacy PascalCase tables with ISNULL null-guard.
/// Postgres/MariaDB: queries snake_case tables with standard frz_ind filter.
/// Ordered by branch name ascending.
/// </summary>
public static class BranchesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/branches", HandleAsync)
             .RequireAuthorization()
             .WithName("Branches");
    }

    private static async Task<IResult> HandleAsync(
        BranchesRequest              request,
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
        string            sql       = PresetsDbHelper.BranchesQuery(presetsDb);

        using IDbConnection conn = connectionFactory.CreateFromConnectionString(
            presetsDb.ConnectionString, presetsDb.DbType);

        IEnumerable<BranchRow> rows = await conn.QueryAsync<BranchRow>(
            sql,
            new { request.TenantId },
            commandTimeout: 10);

        List<BranchRow> list = rows.ToList();
        if (list.Count == 0)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     "No branches found for this tenant.",
                statusCode: StatusCodes.Status404NotFound);

        return TypedResults.Ok(list.Select(r => new
        {
            branch_code  = r.BranchCode,
            branch_name  = r.BranchName,
            company_code = r.CompanyCode,
            company_name = r.CompanyName,
        }));
    }

    // Dapper maps snake_case columns (Postgres/MariaDB) and PascalCase columns (MSSQL)
    // to these properties via case-insensitive underscore-normalised matching.
    private sealed record BranchRow(string BranchCode, string BranchName, string CompanyCode, string CompanyName);

    private sealed record BranchesRequest : RequestContext;
}
