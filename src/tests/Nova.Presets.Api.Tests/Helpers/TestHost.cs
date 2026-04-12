using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nova.Presets.Api.Services;
using Nova.Shared.Security;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace Nova.Presets.Api.Tests.Helpers;

/// <summary>
/// Thin factory for <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// Call <see cref="Create"/> for all Presets.Api tests — no Redis or external container
/// is required since caching is disabled for this service.
///
/// The factory replaces two infrastructure dependencies for test isolation:
/// <list type="bullet">
///   <item><see cref="PassthroughCipherService"/> — removes dependency on ENCRYPTION_KEY;
///         appsettings.json supplies all secrets as plaintext.</item>
///   <item><see cref="NoOpEmailSender"/> — discards outbound email so no SendGrid API key
///         is needed and no email is delivered during test runs.</item>
/// </list>
///
/// Test logs are written to <c>TestResults/Logs/</c> in two formats:
/// <list type="bullet">
///   <item><c>presets-api-test-{date}.json</c> — structured JSON for AI-assisted log review</item>
///   <item><c>presets-api-test-{date}.log</c>  — plain text for human reading during debugging</item>
/// </list>
/// </summary>
public static class TestHost
{
    /// <summary>
    /// Creates a test host for Presets.Api endpoints.
    /// Suitable for all test cases — no external infrastructure is required.
    /// </summary>
    public static WebApplicationFactory<Program> Create() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");

                // Override JWT settings so the test signing key is used regardless of
                // which appsettings.json the WebApplicationFactory loads (it uses the
                // service's content root, not the test project's output directory).
                // AddInMemoryCollection is appended last in the config pipeline, so it
                // takes precedence over all appsettings.json values.
                // These values must match TestConstants.Jwt* and the JwtFactory parameters.
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:SecretKey"]  = TestConstants.JwtSecret,
                        ["Jwt:Issuer"]     = TestConstants.JwtIssuer,
                        ["Jwt:Audience"]   = TestConstants.JwtAudience,
                    });
                });

                ApplyTestLogging(builder);
                builder.ConfigureServices(services =>
                {
                    // Infrastructure-only overrides:
                    // (1) PassthroughCipherService removes the dependency on ENCRYPTION_KEY.
                    //     appsettings.json in this project supplies all secrets as plaintext.
                    services.AddSingleton<ICipherService, PassthroughCipherService>();

                    // (2) NoOpEmailSender prevents outbound email during test runs.
                    //     Tests do not require a SendGrid API key.
                    services.AddSingleton<IEmailSender, NoOpEmailSender>();
                });
            });

    // ---------------------------------------------------------------------------
    // Test logging
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Replaces the application's Serilog logger with a test-specific logger that
    /// writes to TestResults/Logs/ at Information level in two formats.
    ///
    /// Microsoft and System namespaces are capped at Warning to avoid framework noise
    /// swamping application-level entries.
    /// </summary>
    private static void ApplyTestLogging(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging =>
        {
            // Clear the Serilog provider registered by Program.cs → AddNovaLogging.
            // Without this, two Serilog providers run: the production one (writing to
            // logs/audit-.log relative to the build output) and the test one below.
            logging.ClearProviders();
        });

        builder.ConfigureServices(services =>
        {
            services.AddLogging(lb =>
            {
                lb.AddSerilog(BuildTestLogger(), dispose: true);
            });
        });
    }

    private static Serilog.Core.Logger BuildTestLogger()
    {
        // Write to TestResults/Logs/ alongside the TRX file produced by dotnet test.
        // AppContext.BaseDirectory is the test binary output directory (bin/Debug/net10.0/).
        // Navigate up three levels to reach the project root, then into TestResults/Logs/.
        string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        string logDir      = Path.Combine(projectRoot, "TestResults", "Logs");
        Directory.CreateDirectory(logDir);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System",    LogEventLevel.Warning)
            .Enrich.FromLogContext()

            // JSON sink — preserves all structured properties.
            // Use this file for AI-assisted log review.
            .WriteTo.File(
                formatter:       new JsonFormatter(renderMessage: true),
                path:            Path.Combine(logDir, "presets-api-test-.json"),
                rollingInterval: RollingInterval.Day,
                shared:          true)

            // Plain-text sink — human-readable during local debugging.
            .WriteTo.File(
                path:            Path.Combine(logDir, "presets-api-test-.log"),
                rollingInterval: RollingInterval.Day,
                shared:          true,
                outputTemplate:  "{Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ} [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}")

            .CreateLogger();
    }
}
