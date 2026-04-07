using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nova.Shared.Security;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace Nova.Shell.Api.Tests.Helpers;

/// <summary>
/// Thin factory for <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// Call <see cref="Create"/> for stateless tests with no external infrastructure.
/// Call <see cref="CreateWithRedis"/> for tests that exercise cache or locking endpoints.
///
/// Both overloads replace the application's Serilog logger with a test-specific logger that
/// writes to <c>TestResults/Logs/</c> at Information level in two formats:
/// <list type="bullet">
///   <item><c>shell-api-test-{date}.json</c> — structured JSON for AI-assisted log review</item>
///   <item><c>shell-api-test-{date}.log</c> — plain text for human reading during debugging</item>
/// </list>
/// </summary>
public static class TestHost
{
    /// <summary>
    /// Creates a test host for stateless endpoints that have no external infrastructure
    /// dependencies (e.g. GET /api/v1/hello-world, GET /health).
    /// </summary>
    public static WebApplicationFactory<Program> Create() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                ApplyTestLogging(builder);
                builder.ConfigureServices(services =>
                {
                    // Infrastructure-only override: PassthroughCipherService removes the
                    // dependency on the ENCRYPTION_KEY environment variable. appsettings.json
                    // in this project supplies all secrets as plaintext.
                    services.AddSingleton<ICipherService, PassthroughCipherService>();
                });
            });

    /// <summary>
    /// Creates a test host wired to a specific Redis instance.
    /// Use this overload in test classes that implement <see cref="IAsyncLifetime"/>
    /// and start a <see cref="RedisFixture"/> container.
    /// </summary>
    public static WebApplicationFactory<Program> CreateWithRedis(string redisConnectionString) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.UseSetting("ConnectionStrings:redis", redisConnectionString);
                ApplyTestLogging(builder);
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<ICipherService, PassthroughCipherService>();
                });
            });

    // ---------------------------------------------------------------------------
    // Test logging
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Replaces the application's Serilog logger (set by AddNovaLogging in Program.cs)
    /// with a test-specific logger that writes to TestResults/Logs/ at Information level.
    ///
    /// Two sinks are configured:
    ///   - JSON: all structured properties preserved — readable by AI log review tools
    ///   - Plain text: human-readable alongside the JSON during local debugging
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
            // Use this file for AI-assisted log review: every event is a self-contained
            // JSON object with Timestamp, Level, MessageTemplate, RenderedMessage,
            // Exception (with full stack trace), and all Serilog-enriched Properties.
            .WriteTo.File(
                formatter:       new JsonFormatter(renderMessage: true),
                path:            Path.Combine(logDir, "shell-api-test-.json"),
                rollingInterval: RollingInterval.Day,
                shared:          true)

            // Plain-text sink — human-readable during local debugging.
            // Properties block {Properties:j} captures all structured fields inline.
            .WriteTo.File(
                path:            Path.Combine(logDir, "shell-api-test-.log"),
                rollingInterval: RollingInterval.Day,
                shared:          true,
                outputTemplate:  "{Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ} [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}")

            .CreateLogger();
    }
}
