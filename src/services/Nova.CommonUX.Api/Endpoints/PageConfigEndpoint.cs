using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.CommonUX.Api.Endpoints;

/// <summary>
/// Page config: <c>POST /api/v1/page-config</c>
/// Returns per-program UX configuration JSON stored in <c>nova_auth.page_config</c>.
/// Returns empty config <c>{}</c> if no row exists for the requested program_code.
/// </summary>
public static class PageConfigEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/page-config", HandleAsync)
             .RequireAuthorization()
             .WithName("PageConfig");
    }

    private static async Task<IResult> HandleAsync(
        PageConfigRequest            request,
        HttpContext                  httpContext,
        IDbConnectionFactory         connectionFactory,
        IOptions<AuthDbSettings>     authDbOptions,
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

        if (string.IsNullOrWhiteSpace(request.ProgramCode))
            return TypedResults.Problem(
                title:      "Bad Request",
                detail:     "program_code is required.",
                statusCode: StatusCodes.Status400BadRequest);

        AuthDbSettings authDb  = authDbOptions.Value;
        ISqlDialect    dialect = CreateDialect(authDb.DbType);
        string         table   = dialect.TableRef("nova_auth", "page_config");

        string? configJson;
        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(
                   authDb.ConnectionString, authDb.DbType))
        {
            configJson = await conn.QueryFirstOrDefaultAsync<string>(
                $"""
                SELECT config_json
                FROM   {table}
                WHERE  program_code = @ProgramCode
                """,
                new { ProgramCode = request.ProgramCode },
                commandTimeout: 10);
        }

        // Return the raw JSON string embedded as a literal, or an empty object
        object config = string.IsNullOrWhiteSpace(configJson)
            ? new { }
            : System.Text.Json.JsonSerializer.Deserialize<object>(configJson)!;

        return TypedResults.Ok(new
        {
            program_code = request.ProgramCode,
            config,
        });
    }

    private static ISqlDialect CreateDialect(Nova.Shared.Data.DbType dbType) => dbType switch
    {
        Nova.Shared.Data.DbType.Postgres => new PostgresDialect(),
        Nova.Shared.Data.DbType.MariaDb  => new MariaDbDialect(),
        _                                => new MsSqlDialect(),
    };

    private sealed record PageConfigRequest : RequestContext
    {
        public string? ProgramCode { get; init; }
    }
}
