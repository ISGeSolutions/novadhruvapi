using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.Shared.Configuration;
using Nova.Shared.Data;
using NovaDbType = Nova.Shared.Data.DbType;
using Nova.Shared.Logging;
using Nova.Shared.Observability;
using Nova.Shared.Tenancy;
using Nova.Shared.Web.Auth;
using Nova.Shared.Web.Middleware;
using Nova.Shared.Web.Observability;
using Nova.Shared.Web.Tenancy;
using Nova.Shell.Api.Endpoints;
using Nova.Shell.Api.HealthChecks;

bool consoleMode = Environment.GetEnvironmentVariable("RUN_AS_CONSOLE") == "true"
                   || args.Contains("--console");

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

if (consoleMode)
    builder.Host.UseConsoleLifetime();

// 1. Configuration (loads appsettings.json + opsettings.json, validates OpsSettings, registers ICipherService)
builder.AddNovaConfiguration();
if (consoleMode) Console.WriteLine("[Nova] Config files loaded and validated.");

// 2. Logging (Serilog with file sinks)
builder.AddNovaLogging();
if (consoleMode) Console.WriteLine("[Logging] DB sink disabled — enable in opsettings");

// 3. Tenancy (TenantRegistry, scoped TenantContext)
builder.Services.AddNovaTenancy();

// 4. OpenTelemetry (traces + metrics, OTLP export)
builder.AddNovaOpenTelemetry();
builder.AddNovaWebInstrumentation();
AppSettings appSettingsForOtel = new();
builder.Configuration.Bind(appSettingsForOtel);
if (consoleMode) Console.WriteLine($"[OTel] Configured. Endpoint: {appSettingsForOtel.OpenTelemetry.OtlpEndpoint}");

// 5. JWT authentication
builder.AddNovaJwt();

// 6. Data access
builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddHttpContextAccessor();

// 7. Health checks
builder.Services
    .AddHealthChecks()
    .AddCheck<MsSqlHealthCheck>("mssql", tags: ["mssql", "db"])
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["postgres", "db"])
    .AddCheck<MariaDbHealthCheck>("mariadb", tags: ["mariadb", "db"]);

WebApplication app = builder.Build();

// Validate tenant registry and optionally print diagnostic connections
TenantRegistry tenantRegistry = app.Services.GetRequiredService<TenantRegistry>();
if (consoleMode) Console.WriteLine($"[Tenancy] Tenant registry loaded. Tenants: {tenantRegistry.Count}");

// Connectivity pings in console mode
if (consoleMode)
{
    AppSettings appSettings = app.Services.GetRequiredService<IOptions<AppSettings>>().Value;
    OpsSettings opsSettings = app.Services.GetRequiredService<IOptionsMonitor<OpsSettings>>().CurrentValue;
    IDbConnectionFactory factory = app.Services.GetRequiredService<IDbConnectionFactory>();

    if (opsSettings.HealthChecks.DisableMsSql)
    {
        Console.WriteLine("[MSSQL] Connectivity check skipped (disabled via opsettings).");
    }
    else
    {
        try
        {
            using IDbConnection msSqlConn = factory.CreateFromConnectionString(appSettings.DiagnosticConnections.MsSql, NovaDbType.MsSql);
            int mssqlResult = msSqlConn.ExecuteScalar<int>("SELECT 1");
            Console.WriteLine($"[MSSQL] Connectivity OK (result: {mssqlResult})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MSSQL] Connectivity FAILED: {ex.Message}");
        }
    }

    if (opsSettings.HealthChecks.DisablePostgres)
    {
        Console.WriteLine("[Postgres] Connectivity check skipped (disabled via opsettings).");
    }
    else
    {
        try
        {
            using IDbConnection pgConn = factory.CreateFromConnectionString(appSettings.DiagnosticConnections.Postgres, NovaDbType.Postgres);
            int pgResult = pgConn.ExecuteScalar<int>("SELECT 1");
            Console.WriteLine($"[Postgres] Connectivity OK (result: {pgResult})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Postgres] Connectivity FAILED: {ex.Message}");
        }
    }

    if (opsSettings.HealthChecks.DisableMariaDb)
    {
        Console.WriteLine("[MariaDB] Connectivity check skipped (disabled via opsettings).");
    }
    else
    {
        try
        {
            using IDbConnection mariaDbConn = factory.CreateFromConnectionString(appSettings.DiagnosticConnections.MariaDb, NovaDbType.MariaDb);
            int mariaDbResult = mariaDbConn.ExecuteScalar<int>("SELECT 1");
            Console.WriteLine($"[MariaDB] Connectivity OK (result: {mariaDbResult})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MariaDB] Connectivity FAILED: {ex.Message}");
        }
    }
}

// Middleware pipeline
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantResolutionMiddleware>();

// Endpoints
HelloWorldEndpoint.Map(app);
TestDbMsSqlEndpoint.Map(app);
TestDbPostgresEndpoint.Map(app);

// Health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/mssql", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("mssql")
});
app.MapHealthChecks("/health/postgres", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("postgres")
});
app.MapHealthChecks("/health/mariadb", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("mariadb")
});

app.Run();
