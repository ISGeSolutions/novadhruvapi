using Dapper;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Nova.Presets.Api.Configuration;
using Nova.Presets.Api.Endpoints;
using Nova.Presets.Api.Services;
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

// 8. Data access (single IDbConnectionFactory used for both AuthDb and PresetsDb connections)
builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddHttpContextAccessor();

// Dapper global settings + type handlers
DefaultTypeMap.MatchNamesWithUnderscores = true;   // map snake_case columns → PascalCase properties
Nova.Shared.Data.DapperTypeHandlers.RegisterDateOnly();
string? presetsDbType = builder.Configuration["PresetsDb:DbType"];
if (presetsDbType is "MsSql" or "MariaDb")
    Nova.Shared.Data.DapperTypeHandlers.RegisterDateTimeOffset();

// 9. Problem Details (RFC 9457)
builder.Services.AddNovaProblemDetails();

// 10. Per-tenant rate limiting
builder.Services.AddNovaRateLimiting();

// 11. Cache service (framework wiring — caching disabled for this service)
builder.Services.AddNovaCaching();

// 12. Database migrations (DbUp — runs against presets DB via RunPresetsMigrationsEndpoint)
builder.AddNovaMigrations();

// 13. Service-specific settings
builder.Services.Configure<AuthDbSettings>(
    builder.Configuration.GetSection(AuthDbSettings.SectionName));
builder.Services.Configure<PresetsDbSettings>(
    builder.Configuration.GetSection(PresetsDbSettings.SectionName));
builder.Services.Configure<ChangePasswordSettings>(
    builder.Configuration.GetSection(ChangePasswordSettings.SectionName));
builder.Services.Configure<AvatarStorageSettings>(
    builder.Configuration.GetSection(AvatarStorageSettings.SectionName));
builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection(EmailSettings.SectionName));

// 14. Email sender — use NoOp if no API key is configured (local dev without SendGrid)
bool hasSendGridKey = !string.IsNullOrWhiteSpace(
    builder.Configuration["Email:SendGrid:ApiKey"]);

if (hasSendGridKey)
    builder.Services.AddSingleton<IEmailSender, SendGridEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, NoOpEmailSender>();

// 15. Health checks
builder.Services.AddHealthChecks();

// Read avatar storage path now so we can ensure the directory exists before the app starts
AvatarStorageSettings avatarStorage = new();
builder.Configuration.GetSection(AvatarStorageSettings.SectionName).Bind(avatarStorage);
if (!string.IsNullOrWhiteSpace(avatarStorage.LocalDirectory) &&
    !Directory.Exists(avatarStorage.LocalDirectory))
{
    Directory.CreateDirectory(avatarStorage.LocalDirectory);
}

WebApplication app = builder.Build();

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

// Middleware pipeline
app.UseNovaProblemDetails();

// Static files — serves avatar images from LocalDirectory under /avatars
if (!string.IsNullOrWhiteSpace(avatarStorage.LocalDirectory) &&
    Directory.Exists(avatarStorage.LocalDirectory))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.GetFullPath(avatarStorage.LocalDirectory)),
        RequestPath  = "/avatars",
    });
}

app.UseAuthentication();
app.UseAuthorization();
app.UseNovaRateLimiting();

// ---------------------------------------------------------------------------
// Versioned API endpoints — /api/v1/...
// ---------------------------------------------------------------------------

HelloWorldEndpoint.Map(v1);

UserProfileEndpoint.Map(v1);
UploadAvatarEndpoint.Map(v1);
StatusOptionsEndpoint.Map(v1);
UpdateStatusEndpoint.Map(v1);
ChangePasswordEndpoint.Map(v1);
ConfirmPasswordChangeEndpoint.Map(v1);

DefaultPasswordEndpoint.Map(v1);

BranchesEndpoint.Map(v1);
UsersByRoleEndpoint.Map(v1);
TourGenericsEndpoint.Map(v1);
TasksEndpoint.Map(v1);

// ---------------------------------------------------------------------------
// Unversioned diagnostic / admin endpoints
// ---------------------------------------------------------------------------
RunPresetsMigrationsEndpoint.Map(app);
PresetsDbHealthEndpoint.Map(app);

// Health check endpoints
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
