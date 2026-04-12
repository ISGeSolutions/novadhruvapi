using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.Shared.Configuration;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.CommonUX.Api.Endpoints;

/// <summary>
/// Tenant config: <c>POST /api/v1/tenant-config</c>
/// Returns UX/branding config for the current tenant/company/branch.
/// Auth required. <c>ux_version</c> and <c>api_version</c> come from <c>appsettings.json</c>.
/// </summary>
public static class TenantConfigEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/tenant-config", HandleAsync)
             .RequireAuthorization()
             .WithName("TenantConfig");
    }

    private static async Task<IResult> HandleAsync(
        TenantConfigRequest          request,
        HttpContext                  httpContext,
        IDbConnectionFactory         connectionFactory,
        IOptions<AuthDbSettings>     authDbOptions,
        IOptions<AppSettings>        appOptions,
        CancellationToken            ct)
    {
        Dictionary<string, string[]> contextErrors = RequestContextValidator.Validate(request);
        if (contextErrors.Count > 0)
            return TypedResults.ValidationProblem(contextErrors, title: "Validation failed");

        // Validate tenant_id from request matches JWT claim
        string? jwtTenantId = httpContext.User.FindFirstValue("tenant_id");
        if (!string.Equals(request.TenantId, jwtTenantId, StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        AuthDbSettings authDb  = authDbOptions.Value;
        ISqlDialect    dialect = CreateDialect(authDb.DbType);
        string         config  = dialect.TableRef("nova_auth", "tenant_config");

        using IDbConnection connection = connectionFactory.CreateFromConnectionString(
            authDb.ConnectionString, authDb.DbType);

        ConfigRow? row = await connection.QuerySingleOrDefaultAsync<ConfigRow>(
            $"""
            SELECT tenant_id, tenant_name, company_code, company_name, branch_code, branch_name,
                   client_name, client_logo_url, active_users_inline_threshold,
                   unclosed_web_enquiries_url, task_list_url, breadcrumb_position,
                   footer_gradient_refresh_ms, enabled_auth_methods
            FROM {config}
            WHERE tenant_id = @TenantId AND company_code = @CompanyCode AND branch_code = @BranchCode
              AND frz_ind = {dialect.BooleanLiteral(false)}
            """,
            new { request.TenantId, request.CompanyCode, request.BranchCode },
            commandTimeout: 10);

        if (row is null)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     "Tenant configuration not found.",
                statusCode: StatusCodes.Status404NotFound);

        // ux_version and api_version come from appsettings, not DB
        AppSettings app = appOptions.Value;

        // NULL = all methods enabled; stored as comma-separated string in DB.
        string[] authMethods = string.IsNullOrWhiteSpace(row.EnabledAuthMethods)
            ? ["google", "microsoft", "apple", "magic_link"]
            : row.EnabledAuthMethods
                  .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return TypedResults.Ok(new
        {
            tenant_id                      = row.TenantId,
            tenant_name                    = row.TenantName,
            company_code                   = row.CompanyCode,
            company_name                   = row.CompanyName,
            branch_code                      = row.BranchCode,
            branch_name                    = row.BranchName,
            client_name                    = row.ClientName,
            client_logo_url                = row.ClientLogoUrl,
            active_users_inline_threshold  = row.ActiveUsersInlineThreshold,
            unclosed_web_enquiries_url     = row.UnclosedWebEnquiriesUrl,
            task_list_url                  = row.TaskListUrl,
            breadcrumb_position            = row.BreadcrumbPosition,
            footer_gradient_refresh_ms     = row.FooterGradientRefreshMs,
            ux_version                     = "1.0.0",
            api_version                    = "1.0.0",
            enabled_auth_methods           = authMethods,
        });
    }

    private static ISqlDialect CreateDialect(Nova.Shared.Data.DbType dbType) => dbType switch
    {
        Nova.Shared.Data.DbType.Postgres => new PostgresDialect(),
        Nova.Shared.Data.DbType.MariaDb  => new MariaDbDialect(),
        _                                 => new MsSqlDialect()
    };

    private sealed record ConfigRow(
        string  TenantId,
        string  TenantName,
        string  CompanyCode,
        string  CompanyName,
        string  BranchCode,
        string  BranchName,
        string  ClientName,
        string? ClientLogoUrl,
        int     ActiveUsersInlineThreshold,
        string? UnclosedWebEnquiriesUrl,
        string? TaskListUrl,
        string  BreadcrumbPosition,
        int     FooterGradientRefreshMs,
        string? EnabledAuthMethods);

    private sealed record TenantConfigRequest : RequestContext;
}
