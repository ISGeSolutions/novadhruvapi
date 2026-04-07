using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nova.Shared.Configuration;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace Nova.Shared.Observability;

/// <summary>Extension methods for configuring OpenTelemetry traces and metrics.</summary>
public static class OtelSetupExtensions
{
    /// <summary>The <see cref="ActivitySource"/> used throughout the Nova platform.</summary>
    public static readonly ActivitySource NovaActivitySource = new("Nova.Shell");

    /// <summary>
    /// Registers OpenTelemetry with OTLP export and runtime instrumentation.
    /// Call <see cref="Nova.Shared.Web.Observability.WebOtelExtensions.AddNovaWebInstrumentation"/> in web API projects
    /// to add ASP.NET Core-specific instrumentation.
    /// </summary>
    public static IHostApplicationBuilder AddNovaOpenTelemetry(this IHostApplicationBuilder builder)
    {
        AppSettings appSettings = new();
        ((IConfiguration)builder.Configuration).Bind(appSettings);

        string serviceName = appSettings.OpenTelemetry.ServiceName;
        // Prefer Aspire-injected OTEL_EXPORTER_OTLP_ENDPOINT env var; fall back to appsettings.
        string otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
                              ?? appSettings.OpenTelemetry.OtlpEndpoint;
        string version = typeof(OtelSetupExtensions).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        ResourceBuilder resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName, serviceVersion: version)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = builder.Environment.EnvironmentName
            });

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resource)
                    .AddSource("Nova.Shell")
                    .AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resource)
                    .AddRuntimeInstrumentation()
                    .AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
            });

        builder.Services.AddSingleton(NovaActivitySource);

        return builder;
    }
}
