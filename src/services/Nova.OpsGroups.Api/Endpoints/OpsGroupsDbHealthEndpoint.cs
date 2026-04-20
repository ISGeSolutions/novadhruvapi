using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.OpsGroups.Api.Configuration;
using Nova.Shared.Data;

namespace Nova.OpsGroups.Api.Endpoints;

public static class OpsGroupsDbHealthEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/health/db", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        IDbConnectionFactory             connectionFactory,
        IOptions<OpsGroupsDbSettings>    opsGroupsDbOptions,
        CancellationToken                ct)
    {
        OpsGroupsDbSettings opsGroupsDb = opsGroupsDbOptions.Value;
        var                 started     = DateTimeOffset.UtcNow;

        try
        {
            using IDbConnection connection = connectionFactory.CreateFromConnectionString(
                opsGroupsDb.ConnectionString, opsGroupsDb.DbType);

            await connection.ExecuteScalarAsync<int>("SELECT 1");
            long latencyMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

            return Results.Ok(new
            {
                database   = "opsgroups",
                db_type    = opsGroupsDb.DbType.ToString(),
                status     = "healthy",
                latency_ms = latencyMs,
            });
        }
        catch (Exception ex)
        {
            long latencyMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

            return Results.Json(new
            {
                database   = "opsgroups",
                db_type    = opsGroupsDb.DbType.ToString(),
                status     = "unhealthy",
                error      = ex.Message,
                latency_ms = latencyMs,
            },
            statusCode: 503);
        }
    }
}
