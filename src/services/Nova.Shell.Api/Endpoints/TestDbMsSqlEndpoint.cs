using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.Shared.Configuration;
using Nova.Shared.Data;
using NovaDbType = Nova.Shared.Data.DbType;

namespace Nova.Shell.Api.Endpoints;

/// <summary>Maps the <c>GET /test-db/mssql</c> diagnostic endpoint.</summary>
public static class TestDbMsSqlEndpoint
{
    /// <summary>Registers the endpoint on the given <see cref="WebApplication"/>.</summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/test-db/mssql", async (
            IDbConnectionFactory connectionFactory,
            IOptions<AppSettings> appOptions) =>
        {
            AppSettings settings = appOptions.Value;
            MsSqlDialect dialect = new();
            string tableRef = dialect.TableRef("sales97", "pointer");

            try
            {
                using IDbConnection connection = connectionFactory.CreateFromConnectionString(
                    settings.DiagnosticConnections.MsSql,
                    NovaDbType.MsSql);

                string sql = "SELECT code, value FROM " + tableRef;
                IEnumerable<PointerRow> rows = await connection.QueryAsync<PointerRow>(sql);

                return Results.Ok(rows);
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = ex.Message, db = "mssql" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .AllowAnonymous()
        .WithName("TestDbMsSql");
    }

    private sealed record PointerRow(string Code, string Value);
}
