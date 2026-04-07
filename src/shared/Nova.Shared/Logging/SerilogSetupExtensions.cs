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
    /// Configures Serilog with file sinks, enrichers, and a dynamic level switch
    /// driven by <see cref="TimeWindowLevelEvaluator"/> and <see cref="OpsSettings"/>.
    /// </summary>
    public static IHostApplicationBuilder AddNovaLogging(this IHostApplicationBuilder builder)
    {
        OpsSettings initialOps = new();
        builder.Configuration.Bind(initialOps);

        LogEventLevel initialLevel = TimeWindowLevelEvaluator.Evaluate(initialOps.Logging);
        LoggingLevelSwitch levelSwitch = new(initialLevel);

        // Aspire injects OTEL_EXPORTER_OTLP_ENDPOINT — use it for Structured logs in the dashboard.
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

        Log.Logger = loggerConfig.CreateLogger();
        builder.Services.AddSerilog(dispose: true);

        builder.Services.AddSingleton(levelSwitch);

        return builder;
    }
}
