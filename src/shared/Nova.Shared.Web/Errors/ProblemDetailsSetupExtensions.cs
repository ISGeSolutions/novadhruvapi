using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Nova.Shared.Web.Errors;

/// <summary>
/// Extension methods for configuring RFC 9457 Problem Details error responses.
/// </summary>
public static class ProblemDetailsSetupExtensions
{
    private const string CorrelationIdKey = "X-Correlation-ID";

    /// <summary>
    /// Registers Problem Details services. Unhandled exceptions return RFC 9457 JSON —
    /// no stack traces are exposed to clients. Every error response is enriched with
    /// <c>correlation_id</c> (from <see cref="CorrelationIdMiddleware"/>) and
    /// <c>trace_id</c> (from the active <see cref="Activity"/> or the ASP.NET Core
    /// trace identifier) for cross-service diagnostics.
    /// </summary>
    public static IServiceCollection AddNovaProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                if (ctx.HttpContext.Items[CorrelationIdKey] is string correlationId
                    && !string.IsNullOrEmpty(correlationId))
                {
                    ctx.ProblemDetails.Extensions["correlation_id"] = correlationId;
                }

                ctx.ProblemDetails.Extensions["trace_id"] =
                    Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;

                // Suppress instance — avoids leaking internal server paths to clients.
                ctx.ProblemDetails.Instance = null;
            };
        });

        return services;
    }

    /// <summary>
    /// Adds the global exception handler and status code pages to the middleware pipeline.
    /// Must be called before all other middleware so that every unhandled exception and
    /// every non-exception 4xx/5xx response is formatted as Problem Details.
    /// </summary>
    public static IApplicationBuilder UseNovaProblemDetails(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }
}
