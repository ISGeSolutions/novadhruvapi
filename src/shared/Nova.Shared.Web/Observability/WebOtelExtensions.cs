using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Nova.Shared.Web.Observability;

/// <summary>
/// Extends Nova OpenTelemetry setup with ASP.NET Core-specific instrumentation.
/// Call this in addition to <see cref="OtelSetupExtensions.AddNovaOpenTelemetry"/> in web API projects.
/// </summary>
public static class WebOtelExtensions
{
    /// <summary>
    /// Adds ASP.NET Core trace and metric instrumentation to the OpenTelemetry configuration.
    /// Must be called after <see cref="OtelSetupExtensions.AddNovaOpenTelemetry"/>.
    /// </summary>
    public static WebApplicationBuilder AddNovaWebInstrumentation(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
            tracing.AddAspNetCoreInstrumentation());

        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
            metrics.AddAspNetCoreInstrumentation());

        return builder;
    }
}
