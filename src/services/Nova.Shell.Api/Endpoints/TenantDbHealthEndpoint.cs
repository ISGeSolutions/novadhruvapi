using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Security;
using Nova.Shared.Tenancy;

namespace Nova.Shell.Api.Endpoints;

/// <summary>
/// Reports database connectivity for a single tenant.
/// </summary>
/// <remarks>
/// Unlike <c>/health/mssql</c> (which tests the shared diagnostic connection),
/// this endpoint opens a connection using the tenant's own encrypted connection string.
/// Useful for verifying a specific tenant's DB is reachable after provisioning or migration.
///
/// <para><b>Route</b></para>
/// <c>GET /health/db/{tenantId}</c>
/// </remarks>
public static class TenantDbHealthEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/health/db/{tenantId}", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string               tenantId,
        TenantRegistry       tenants,
        IDbConnectionFactory factory,
        ICipherService       cipher,
        CancellationToken    ct)
    {
        TenantRecord? tenant = tenants.All
            .FirstOrDefault(t => t.TenantId.Equals(tenantId, StringComparison.OrdinalIgnoreCase));

        if (tenant is null)
            return Results.NotFound(new { error = $"Tenant '{tenantId}' not found." });

        var started = DateTimeOffset.UtcNow;

        try
        {
            string rawConnStr = cipher.Decrypt(tenant.ConnectionString);

            using IDbConnection conn = factory.OpenRaw(rawConnStr, tenant.DbType);
            await conn.ExecuteScalarAsync<int>("SELECT 1");

            long latencyMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

            return Results.Ok(new
            {
                tenant_id  = tenant.TenantId,
                db_type    = tenant.DbType.ToString(),
                status     = "healthy",
                latency_ms = latencyMs,
            });
        }
        catch (Exception ex)
        {
            long latencyMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

            return Results.Json(new
            {
                tenant_id  = tenant.TenantId,
                db_type    = tenant.DbType.ToString(),
                status     = "unhealthy",
                error      = ex.Message,
                latency_ms = latencyMs,
            },
            statusCode: 503);
        }
    }
}
