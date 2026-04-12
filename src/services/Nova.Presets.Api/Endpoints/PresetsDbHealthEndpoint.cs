using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.Presets.Api.Configuration;
using Nova.Shared.Data;

namespace Nova.Presets.Api.Endpoints;

/// <summary>
/// Reports connectivity to the <c>presets</c> database.
/// <para><b>Route</b></para>
/// <c>GET /health/db</c>
/// </summary>
public static class PresetsDbHealthEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/health/db", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        IDbConnectionFactory          connectionFactory,
        IOptions<PresetsDbSettings>   presetsDbOptions,
        CancellationToken             ct)
    {
        PresetsDbSettings presetsDb = presetsDbOptions.Value;
        var               started   = DateTimeOffset.UtcNow;

        try
        {
            using IDbConnection connection = connectionFactory.CreateFromConnectionString(
                presetsDb.ConnectionString, presetsDb.DbType);

            await connection.ExecuteScalarAsync<int>("SELECT 1");
            long latencyMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

            return Results.Ok(new
            {
                database   = "presets",
                db_type    = presetsDb.DbType.ToString(),
                status     = "healthy",
                latency_ms = latencyMs,
            });
        }
        catch (Exception ex)
        {
            long latencyMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

            return Results.Json(new
            {
                database   = "presets",
                db_type    = presetsDb.DbType.ToString(),
                status     = "unhealthy",
                error      = ex.Message,
                latency_ms = latencyMs,
            },
            statusCode: 503);
        }
    }
}
