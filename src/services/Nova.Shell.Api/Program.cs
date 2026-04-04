using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.Shared.Configuration;
using Nova.Shared.Data;
using NovaDbType = Nova.Shared.Data.DbType;
using Nova.Shared.Logging;
using Nova.Shared.Observability;
using Nova.Shared.Tenancy;
using Asp.Versioning;
using Nova.Shared.Web.Auth;
using Nova.Shared.Web.Errors;
using Nova.Shared.Web.Http;
using Nova.Shared.Web.Middleware;
using Nova.Shared.Web.Observability;
using Nova.Shared.Web.Caching;
using Nova.Shared.Web.Locking;
using Nova.Shared.Web.Messaging;
using Nova.Shared.Web.Migrations;
using Nova.Shared.Web.RateLimiting;
using Nova.Shared.Web.Serialisation;
using Nova.Shared.Web.Tenancy;
using Nova.Shared.Web.Versioning;
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

// 5. JWT authentication (user tokens — aud=nova-api)
builder.AddNovaJwt();

// 5b. Internal service-to-service authentication (InternalJwt scheme — aud=nova-internal)
builder.AddNovaInternalAuth();

// 6. JSON serialisation (snake_case wire format — all responses and request binding)
builder.Services.AddNovaJsonOptions();

// 7. API versioning (URL segment: /api/v{n}/resource)
builder.Services.AddNovaApiVersioning();

// 8. Data access
builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddHttpContextAccessor();

// 9. Problem Details (RFC 9457 error responses)
builder.Services.AddNovaProblemDetails();

// 10. Outbound HTTP clients (resilient: retry, circuit breaker, timeouts + correlation ID forwarding)
builder.Services.AddNovaHttpClient("nova-shell",
    builder.Configuration["Services:NovaShell:BaseUrl"] ?? "http://localhost:5100");

// 10b. Internal HTTP client — same as above but attaches InternalJwt Bearer token automatically
builder.Services.AddNovaInternalHttpClient("nova-shell-internal",
    builder.Configuration["Services:NovaShell:BaseUrl"] ?? "http://localhost:5100");

// 11. Per-tenant rate limiting (fixed window, partitioned by tenant_id claim or IP for anonymous)
builder.Services.AddNovaRateLimiting();

// 12. Redis — IConnectionMultiplexer (Aspire injects connection string; standalone uses ConnectionStrings:redis)
builder.AddRedisClient("redis");

// 13. Cache service (ICacheService → RedisCacheService; profiles configured in opsettings.json → Caching)
builder.Services.AddNovaCaching();

// 14. Distributed locking (IDistributedLockService → RedisDistributedLockService; SET NX + Lua release)
builder.Services.AddNovaDistributedLocking();

// 16. Database migrations (DbUp — per-tenant, safe scripts auto-run, destructive scripts blocked + logged)
builder.AddNovaMigrations();

// 17. Outbox relay (polls nova_outbox per tenant, publishes to RabbitMQ or Redis Streams)
builder.AddNovaOutboxRelay();

// 15. Health checks
// Redis health check is registered automatically by AddRedisClient above.
// DB health is per-tenant via GET /health/db/{tenantId}.
builder.Services
    .AddHealthChecks()
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["rabbitmq"]);

WebApplication app = builder.Build();

// Shared API version set — declares v1 exists and opts into api-supported-versions headers.
var novaVersionSet = app.NewApiVersionSet("Nova")
    .HasApiVersion(new ApiVersion(1, 0))
    .ReportApiVersions()
    .Build();

// Versioned route group: all business API endpoints are registered under this prefix.
// Each endpoint Map() call uses the group, not the app directly.
RouteGroupBuilder v1 = app.MapGroup("/api/v{version:apiVersion}")
    .WithApiVersionSet(novaVersionSet)
    .MapToApiVersion(new ApiVersion(1, 0))
    .RequireRateLimiting(RateLimitingExtensions.PolicyName);  // applies to all business endpoints

// Validate tenant registry and optionally print diagnostic connections
TenantRegistry tenantRegistry = app.Services.GetRequiredService<TenantRegistry>();
if (consoleMode) Console.WriteLine($"[Tenancy] Tenant registry loaded. Tenants: {tenantRegistry.Count}");

// Connectivity pings in console mode
if (consoleMode)
{
    AppSettings          appSettings = app.Services.GetRequiredService<IOptions<AppSettings>>().Value;
    IDbConnectionFactory factory     = app.Services.GetRequiredService<IDbConnectionFactory>();

    foreach (var (entry, dbType, label) in new[]
    {
        (appSettings.DiagnosticConnections.MsSql,    NovaDbType.MsSql,    "MSSQL"),
        (appSettings.DiagnosticConnections.Postgres, NovaDbType.Postgres, "Postgres"),
        (appSettings.DiagnosticConnections.MariaDb,  NovaDbType.MariaDb,  "MariaDB"),
    })
    {
        if (!entry.Enabled) { Console.WriteLine($"[{label}] Ping skipped (Enabled: false in appsettings)."); continue; }
        try
        {
            using IDbConnection conn = factory.CreateFromConnectionString(entry.ConnectionString, dbType);
            int result = conn.ExecuteScalar<int>("SELECT 1");
            Console.WriteLine($"[{label}] Connectivity OK (result: {result})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{label}] Connectivity FAILED: {ex.Message}");
        }
    }
}

// Middleware pipeline
app.UseNovaProblemDetails();  // must be first — catches all downstream exceptions
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseNovaRateLimiting();   // after auth — HttpContext.User is populated when partition key is evaluated
app.UseMiddleware<TenantResolutionMiddleware>();

// Versioned API endpoints — registered on the v1 group (/api/v1/...)
HelloWorldEndpoint.Map(v1);
EchoEndpoint.Map(v1);           // reference: validation + Problem Details pattern
EchoListEndpoint.Map(v1);       // reference: pagination contract pattern
HttpPingEndpoint.Map(v1);       // reference: resilient outbound HttpClient pattern

// Unversioned diagnostic endpoints — registered directly on app (no version check applied)
TestDbMsSqlEndpoint.Map(app);
TestDbPostgresEndpoint.Map(app);
TestCacheEndpoint.Map(app);
TestLockEndpoint.Map(app);
TestInternalAuthEndpoint.Map(app);

// Admin endpoints — unversioned, unprotected in dev (add InternalService policy in production)
RunMigrationsEndpoint.Map(app);
TenantDbHealthEndpoint.Map(app);

// Health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/redis",    new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name == "redis"
});
app.MapHealthChecks("/health/rabbitmq", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("rabbitmq")
});
// Per-tenant DB health: GET /health/db/{tenantId}

app.Run();
