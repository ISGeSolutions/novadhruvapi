using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.Shared.Configuration;
using Nova.Shared.Data;
using NovaDbType = Nova.Shared.Data.DbType;

namespace Nova.Shell.Api.Endpoints;

/// <summary>Maps the <c>GET /test-db/postgres</c> diagnostic endpoint.</summary>
public static class TestDbPostgresEndpoint
{
    /// <summary>Registers the endpoint on the given <see cref="WebApplication"/>.</summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/test-db/postgres", async (
            IDbConnectionFactory connectionFactory,
            IOptions<AppSettings> appOptions) =>
        {
            AppSettings settings = appOptions.Value;
            PostgresDialect dialect = new();
            string tableRef = dialect.TableRef("sales97", "pointer");

            try
            {
                if (!settings.DiagnosticConnections.Postgres.Enabled)
                    return Results.Json(new { error = "Postgres diagnostic connection is disabled.", db = "postgres" },
                        statusCode: StatusCodes.Status503ServiceUnavailable);

                using IDbConnection connection = connectionFactory.CreateFromConnectionString(
                    settings.DiagnosticConnections.Postgres.ConnectionString,
                    NovaDbType.Postgres);

                string sql = "SELECT code, value FROM " + tableRef;
                IEnumerable<PointerRow> rows = await connection.QueryAsync<PointerRow>(sql);

                return Results.Ok(rows);
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = ex.Message, db = "postgres" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .AllowAnonymous()
        .WithName("TestDbPostgres");
    }

    private sealed record PointerRow(string Code, string Value);
}
