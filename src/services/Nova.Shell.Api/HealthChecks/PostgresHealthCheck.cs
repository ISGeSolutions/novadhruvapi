using System.Data;
using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Nova.Shared.Configuration;
using Nova.Shared.Data;
using NovaDbType = Nova.Shared.Data.DbType;

namespace Nova.Shell.Api.HealthChecks;

/// <summary>Health check that verifies connectivity to the diagnostic PostgreSQL connection.</summary>
public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly AppSettings _settings;
    private readonly IOptionsMonitor<OpsSettings> _opsSettings;

    /// <summary>Initialises the health check.</summary>
    public PostgresHealthCheck(
        IDbConnectionFactory connectionFactory,
        IOptions<AppSettings> options,
        IOptionsMonitor<OpsSettings> opsSettings)
    {
        _connectionFactory = connectionFactory;
        _settings = options.Value;
        _opsSettings = opsSettings;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_opsSettings.CurrentValue.HealthChecks.DisablePostgres)
            return HealthCheckResult.Degraded("PostgreSQL health check suppressed via opsettings.");

        try
        {
            using IDbConnection connection = await _connectionFactory.CreateFromConnectionStringAsync(
                _settings.DiagnosticConnections.Postgres,
                NovaDbType.Postgres,
                cancellationToken);

            int result = await connection.ExecuteScalarAsync<int>("SELECT 1");
            return result == 1
                ? HealthCheckResult.Healthy("PostgreSQL connection is healthy.")
                : HealthCheckResult.Degraded("PostgreSQL returned unexpected result.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL connection failed.", ex);
        }
    }
}
