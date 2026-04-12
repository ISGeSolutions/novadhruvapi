using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.CommonUX.Api.Endpoints;
using Nova.CommonUX.Api.Endpoints.Auth;
using Nova.CommonUX.Api.Services;
using Nova.Shared.Configuration;
using Nova.Shared.Data;
using Nova.Shared.Observability;
using Nova.Shared.Logging;
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

// 1. Configuration (loads appsettings.json + opsettings.json, validates OpsSettings, registers ICipherService)
builder.AddNovaConfiguration();
if (consoleMode) Console.WriteLine("[Nova] Config files loaded and validated.");

// 2. Logging (Serilog with file sinks)
builder.AddNovaLogging();

// 3. OpenTelemetry (traces + metrics, OTLP export)
builder.AddNovaOpenTelemetry();
builder.AddNovaWebInstrumentation();
AppSettings appSettingsForOtel = new();
builder.Configuration.Bind(appSettingsForOtel);
if (consoleMode) Console.WriteLine($"[OTel] Configured. Endpoint: {appSettingsForOtel.OpenTelemetry.OtlpEndpoint}");

// 4. JWT authentication (Bearer — validates tokens issued by this service)
builder.AddNovaJwt();

// 5. Internal service-to-service authentication
builder.AddNovaInternalAuth();

// 6. JSON serialisation (snake_case wire format)
builder.Services.AddNovaJsonOptions();

// 7. API versioning
builder.Services.AddNovaApiVersioning();

// 8. Data access (IDbConnectionFactory — shared library, used for nova_auth DB)
builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddHttpContextAccessor();

// 9. Problem Details (RFC 9457)
builder.Services.AddNovaProblemDetails();

// 10. Per-tenant rate limiting
builder.Services.AddNovaRateLimiting();

// 11. Redis — IConnectionMultiplexer
builder.AddRedisClient("redis");

// 12. Cache service (ICacheService — for general endpoint caching; not used by auth session store)
builder.Services.AddNovaCaching();

// 13. Database migrations (DbUp — runs against nova_auth via synthetic TenantRecord)
builder.AddNovaMigrations();

// 14. Service-specific settings
builder.Services.Configure<AuthDbSettings>(
    builder.Configuration.GetSection(AuthDbSettings.SectionName));
builder.Services.Configure<AuthSettings>(
    builder.Configuration.GetSection(AuthSettings.SectionName));
builder.Services.Configure<CacheProviderSettings>(
    builder.Configuration.GetSection(CacheProviderSettings.SectionName));
builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection(EmailSettings.SectionName));
builder.Services.Configure<SocialLoginSettings>(
    builder.Configuration.GetSection(SocialLoginSettings.SectionName));

// 15. Token service (JWT issuance)
builder.Services.AddSingleton<ITokenService, TokenService>();

// 16. Session store — InMemory or Redis based on opsettings.json → Cache.CacheProvider
string? cacheProvider = builder.Configuration["Cache:CacheProvider"];
if (string.Equals(cacheProvider, "Redis", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<ISessionStore, RedisSessionStore>();
else
    builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();

// 17. Email sender
builder.Services.AddSingleton<IEmailSender, SendGridEmailSender>();

// 18. Social token verifier
builder.Services.AddSingleton<ISocialTokenVerifier, SocialTokenVerifier>();
builder.Services.AddHttpClient(); // IHttpClientFactory for SocialTokenVerifier

// 19. Health checks
builder.Services.AddHealthChecks();

WebApplication app = builder.Build();

// Shared API version set
var novaVersionSet = app.NewApiVersionSet("Nova")
    .HasApiVersion(new ApiVersion(1, 0))
    .ReportApiVersions()
    .Build();

// Versioned route group: all business API endpoints registered under /api/v1/...
RouteGroupBuilder v1 = app.MapGroup("/api/v{version:apiVersion}")
    .WithApiVersionSet(novaVersionSet)
    .MapToApiVersion(new ApiVersion(1, 0))
    .RequireRateLimiting(RateLimitingExtensions.PolicyName);

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
app.UseNovaProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.UseNovaRateLimiting();

// ---------------------------------------------------------------------------
// Versioned API endpoints — /api/v1/...
// ---------------------------------------------------------------------------

// Liveness
HelloWorldEndpoint.Map(v1);

// Auth endpoints — no Bearer token required (they are the source of tokens)
TokenEndpoint.Map(v1);
LoginEndpoint.Map(v1);
Verify2FaEndpoint.Map(v1);
ForgotPasswordEndpoint.Map(v1);
ResetPasswordEndpoint.Map(v1);
MagicLinkEndpoint.Map(v1);
MagicLinkVerifyEndpoint.Map(v1);
SocialInitiateEndpoint.Map(v1);
SocialCompleteEndpoint.Map(v1);

// Auth endpoints — Bearer token required
RefreshEndpoint.Map(v1);
SocialLinkInitiateEndpoint.Map(v1);
SocialLinkCompleteEndpoint.Map(v1);

// Config endpoints — Bearer token required
TenantConfigEndpoint.Map(v1);
MainAppMenusEndpoint.Map(v1);

// ---------------------------------------------------------------------------
// Unversioned diagnostic / admin endpoints
// ---------------------------------------------------------------------------
RunAuthMigrationsEndpoint.Map(app);
AuthDbHealthEndpoint.Map(app);

// Health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/redis", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name == "redis"
});

app.Run();
