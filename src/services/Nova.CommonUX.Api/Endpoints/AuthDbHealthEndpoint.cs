using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.Shared.Data;

namespace Nova.CommonUX.Api.Endpoints;

/// <summary>
/// Reports connectivity to the <c>nova_auth</c> database.
/// <para><b>Route</b></para>
/// <c>GET /health/db</c>
/// </summary>
public static class AuthDbHealthEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/health/db", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        IDbConnectionFactory     connectionFactory,
        IOptions<AuthDbSettings> authDbOptions,
        CancellationToken        ct)
    {
        AuthDbSettings authDb  = authDbOptions.Value;
        var            started = DateTimeOffset.UtcNow;

        try
        {
            using IDbConnection connection = connectionFactory.CreateFromConnectionString(
                authDb.ConnectionString, authDb.DbType);

            await connection.ExecuteScalarAsync<int>("SELECT 1");
            long latencyMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

            return Results.Ok(new
            {
                database   = "nova_auth",
                db_type    = authDb.DbType.ToString(),
                status     = "healthy",
                latency_ms = latencyMs,
            });
        }
        catch (Exception ex)
        {
            long latencyMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

            return Results.Json(new
            {
                database   = "nova_auth",
                db_type    = authDb.DbType.ToString(),
                status     = "unhealthy",
                error      = ex.Message,
                latency_ms = latencyMs,
            },
            statusCode: 503);
        }
    }
}
