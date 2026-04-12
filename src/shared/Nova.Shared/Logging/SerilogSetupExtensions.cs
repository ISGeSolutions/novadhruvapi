using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nova.Shared.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Nova.Shared.Logging;

/// <summary>Extension methods for configuring Serilog on the host builder.</summary>
public static class SerilogSetupExtensions
{
    /// <summary>
    /// Configures Serilog for all Nova services. Call once from each service's <c>Program.cs</c>.
    ///
    /// <para><b>Sink pipeline (always active):</b></para>
    /// <list type="bullet">
    ///   <item>Console — human-readable, for <c>dotnet run</c> and container stdout.</item>
    ///   <item>File: <c>logs/audit-.log</c> — rolling daily, Information+. Permanent audit trail.</item>
    /// </list>
    ///
    /// <para><b>Sink pipeline (conditionally active):</b></para>
    /// <list type="bullet">
    ///   <item>File: <c>logs/debug-.log</c> — rolling daily, Debug+.
    ///         Active when <c>opsettings.json Logging.EnableDiagnosticLogging = true</c>.</item>
    ///   <item>OpenTelemetry (OTLP) — streams logs to the Aspire dashboard in real time.
    ///         Active when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> env var is set (injected by Aspire automatically).</item>
    ///   <item>Seq — persists structured logs for cross-service querying at <c>http://localhost:5341</c>.
    ///         Active when <c>ConnectionStrings:seq</c> is present (injected by Aspire when
    ///         <c>Infrastructure:UseSeq = true</c> in <c>Nova.AppHost/appsettings.json</c>).
    ///         Silently skipped outside Aspire: tests, <c>dotnet run</c> standalone.</item>
    /// </list>
    ///
    /// <para><b>Sink ownership:</b> all sink decisions live here in <c>Nova.Shared</c>.
    /// <c>Nova.AppHost</c> owns which infrastructure containers start and injects their endpoints;
    /// this method consumes those endpoints. Services call <c>AddNovaLogging()</c> and are
    /// otherwise unaware of what sinks are active.</para>
    /// </summary>
    public static IHostApplicationBuilder AddNovaLogging(this IHostApplicationBuilder builder)
    {
        OpsSettings initialOps = new();
        builder.Configuration.Bind(initialOps);

        LogEventLevel initialLevel = TimeWindowLevelEvaluator.Evaluate(initialOps.Logging);
        LoggingLevelSwitch levelSwitch = new(initialLevel);

        // Injected by Aspire automatically when the service is started via Nova.AppHost.
        string? otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        LoggerConfiguration loggerConfig = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/audit-.log",
                rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: LogEventLevel.Information,
                outputTemplate: "{Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");

        if (initialOps.Logging.EnableDiagnosticLogging)
        {
            loggerConfig.WriteTo.File(
                path: "logs/debug-.log",
                rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: LogEventLevel.Debug,
                outputTemplate: "{Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
        }

        if (!string.IsNullOrEmpty(otlpEndpoint))
        {
            loggerConfig.WriteTo.OpenTelemetry(
                endpoint: otlpEndpoint.TrimEnd('/') + "/v1/logs",
                protocol: Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc);
        }

        // Seq — injected by Aspire as ConnectionStrings:seq when UseSeq = true in AppHost.
        // Silently skipped when running outside Aspire (tests, standalone dotnet run).
        string? seqUrl = builder.Configuration.GetConnectionString("seq");
        if (!string.IsNullOrEmpty(seqUrl))
        {
            loggerConfig.WriteTo.Seq(seqUrl);
        }

        Log.Logger = loggerConfig.CreateLogger();
        builder.Services.AddSerilog(dispose: true);

        builder.Services.AddSingleton(levelSwitch);

        return builder;
    }
}
