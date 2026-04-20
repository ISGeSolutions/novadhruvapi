using Dapper;
using Microsoft.Extensions.Options;
using Nova.OpsGroups.Api.Configuration;
using Nova.OpsGroups.Api.Endpoints;
using Nova.OpsGroups.Api.Endpoints.BusinessRules;
using Nova.OpsGroups.Api.Endpoints.Departures;
using Nova.OpsGroups.Api.Endpoints.GroupTasks;
using Nova.OpsGroups.Api.Endpoints.SlaRules;
using Nova.OpsGroups.Api.Endpoints.Summary;
using Nova.Shared.Configuration;
using Nova.Shared.Data;
using Nova.Shared.Logging;
using Nova.Shared.Observability;
using Nova.Shared.Web.Auth;
using Nova.Shared.Web.Caching;
using Nova.Shared.Web.Errors;
using Nova.Shared.Web.Migrations;
using Nova.Shared.Web.Observability;
using Nova.Shared.Web.RateLimiting;
using Nova.Shared.Web.Serialisation;
using Nova.Shared.Web.Versioning;
using Asp.Versioning;
using NovaDbType = Nova.Shared.Data.DbType;

bool consoleMode = Environment.GetEnvironmentVariable("RUN_AS_CONSOLE") == "true"
                   || args.Contains("--console");

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

if (consoleMode)
    builder.Host.UseConsoleLifetime();

// 1. Configuration
builder.AddNovaConfiguration();
if (consoleMode) Console.WriteLine("[Nova] Config files loaded and validated.");

// 2. Logging
builder.AddNovaLogging();

// 3. OpenTelemetry
builder.AddNovaOpenTelemetry();
builder.AddNovaWebInstrumentation();
AppSettings appSettingsForOtel = new();
builder.Configuration.Bind(appSettingsForOtel);
if (consoleMode) Console.WriteLine($"[OTel] Configured. Endpoint: {appSettingsForOtel.OpenTelemetry.OtlpEndpoint}");

// 4. JWT authentication (validates tokens issued by Nova.CommonUX.Api)
builder.AddNovaJwt();

// 5. Internal service-to-service authentication
builder.AddNovaInternalAuth();

// 6. JSON serialisation (snake_case wire format)
builder.Services.AddNovaJsonOptions();

// 7. API versioning
builder.Services.AddNovaApiVersioning();

// 8. Data access
builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddHttpContextAccessor();

// Dapper global settings + type handlers
DefaultTypeMap.MatchNamesWithUnderscores = true;
Nova.Shared.Data.DapperTypeHandlers.RegisterDateOnly();
string? opsGroupsDbType = builder.Configuration["OpsGroupsDb:DbType"];
if (opsGroupsDbType is "MsSql" or "MariaDb")
    Nova.Shared.Data.DapperTypeHandlers.RegisterDateTimeOffset();

// 9. Problem Details (RFC 9457)
builder.Services.AddNovaProblemDetails();

// 10. Per-tenant rate limiting
builder.Services.AddNovaRateLimiting();

// 11. Cache service
builder.Services.AddNovaCaching();

// 12. Database migrations (DbUp)
builder.AddNovaMigrations();

// 13. Service-specific settings
builder.Services.Configure<AuthDbSettings>(
    builder.Configuration.GetSection(AuthDbSettings.SectionName));
builder.Services.Configure<OpsGroupsDbSettings>(
    builder.Configuration.GetSection(OpsGroupsDbSettings.SectionName));
builder.Services.Configure<PresetsDbSettings>(
    builder.Configuration.GetSection(PresetsDbSettings.SectionName));

// 14. Health checks
builder.Services.AddHealthChecks();

WebApplication app = builder.Build();

// Console-mode diagnostics
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
            using var conn = factory.CreateFromConnectionString(entry.ConnectionString, dbType);
            int result = conn.ExecuteScalar<int>("SELECT 1");
            Console.WriteLine($"[{label}] Connectivity OK (result: {result})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{label}] Connectivity FAILED: {ex.Message}");
        }
    }
}

// Shared API version set
var novaVersionSet = app.NewApiVersionSet("Nova")
    .HasApiVersion(new ApiVersion(1, 0))
    .ReportApiVersions()
    .Build();

// Versioned route group: all business API endpoints under /api/v1/...
RouteGroupBuilder v1 = app.MapGroup("/api/v{version:apiVersion}")
    .WithApiVersionSet(novaVersionSet)
    .MapToApiVersion(new ApiVersion(1, 0))
    .RequireRateLimiting(RateLimitingExtensions.PolicyName);

// Middleware pipeline
app.UseNovaProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.UseNovaRateLimiting();

// ---------------------------------------------------------------------------
// Versioned API endpoints — /api/v1/...
// ---------------------------------------------------------------------------

HelloWorldEndpoint.Map(v1);

// Departures (specific literal route before parameterised)
DeparturesEndpoint.Map(v1);

// Group tasks
GroupTaskEndpoint.Map(v1);

// SLA rules
SlaRulesEndpoint.Map(v1);
SlaHierarchyEndpoint.Map(v1);

// Summary / dashboard
SummaryStatsEndpoint.Map(v1);
DashboardEndpoints.Map(v1);

// Business rules
BusinessRulesEndpoint.Map(v1);

// 410 Gone stubs for removed/relocated endpoints
RemovedEndpointsEndpoint.Map(v1);

// ---------------------------------------------------------------------------
// Unversioned diagnostic / admin endpoints
// ---------------------------------------------------------------------------
RunOpsGroupsMigrationsEndpoint.Map(app);
OpsGroupsDbHealthEndpoint.Map(app);

// ---------------------------------------------------------------------------
// Health check
// ---------------------------------------------------------------------------
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = WriteJsonHealthResponse,
});

app.Run();

static Task WriteJsonHealthResponse(
    HttpContext                                            ctx,
    Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
    ctx.Response.ContentType = "application/json; charset=utf-8";
    return ctx.Response.WriteAsync($"{{\"status\":\"{report.Status}\"}}");
}
