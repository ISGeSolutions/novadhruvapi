using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nova.Shared.Configuration;

namespace Nova.Shared.Web.RateLimiting;

/// <summary>
/// Extension methods for per-tenant rate limiting using the built-in ASP.NET Core rate limiter.
/// </summary>
/// <remarks>
/// Policy: <see cref="PolicyName"/> (fixed window, per tenant or per IP for anonymous requests).
/// Apply to route groups via <c>.RequireRateLimiting(RateLimitingExtensions.PolicyName)</c>.
/// Controlled entirely from <c>opsettings.json → RateLimiting</c> — changes take effect
/// immediately without a restart.
/// </remarks>
public static class RateLimitingExtensions
{
    /// <summary>The rate limiting policy name. Apply to versioned route groups in Program.cs.</summary>
    public const string PolicyName = "nova-per-tenant";

    private const string CorrelationIdKey = "X-Correlation-ID";

    /// <summary>
    /// Registers the per-tenant fixed-window rate limiter.
    /// <para>
    /// Partition key: <c>tenant:{tenantId}</c> for authenticated requests,
    /// <c>ip:{remoteIp}</c> for anonymous requests (health checks etc. are not rate-limited
    /// because they are registered outside the versioned route group).
    /// </para>
    /// <para>
    /// Rejected requests receive <c>429 Too Many Requests</c> with an
    /// <c>application/problem+json</c> body and a <c>Retry-After</c> header.
    /// </para>
    /// </summary>
    public static IServiceCollection AddNovaRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                HttpContext httpContext = context.HttpContext;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                    httpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

                string correlationId = httpContext.Items[CorrelationIdKey] as string ?? string.Empty;
                string traceId       = Activity.Current?.Id ?? httpContext.TraceIdentifier;

                httpContext.Response.ContentType = "application/problem+json";

                string body = JsonSerializer.Serialize(new
                {
                    type           = "https://tools.ietf.org/html/rfc6585#section-4",
                    title          = "Too Many Requests",
                    status         = StatusCodes.Status429TooManyRequests,
                    detail         = "Request rate limit exceeded. Please reduce your request rate.",
                    correlation_id = correlationId,
                    trace_id       = traceId
                });

                await httpContext.Response.WriteAsync(body, cancellationToken);
            };

            options.AddPolicy(PolicyName, httpContext =>
            {
                // Read current settings on every request — Enabled/PermitLimit/WindowSeconds
                // changes in opsettings.json take effect immediately without a restart.
                IOptionsMonitor<OpsSettings> monitor =
                    httpContext.RequestServices.GetRequiredService<IOptionsMonitor<OpsSettings>>();
                OpsRateLimitingSettings settings = monitor.CurrentValue.RateLimiting;

                if (!settings.Enabled)
                    return RateLimitPartition.GetNoLimiter<string>("disabled");

                // Partition by tenant_id claim for authenticated requests.
                // Anonymous requests (anything outside the versioned group that has RequireRateLimiting)
                // will not normally hit this policy, but fall back to IP if they do.
                string partitionKey = httpContext.User.Identity?.IsAuthenticated == true
                    ? $"tenant:{httpContext.User.FindFirstValue("tenant_id") ?? "unknown"}"
                    : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit          = settings.PermitLimit,
                        Window               = TimeSpan.FromSeconds(settings.WindowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit           = settings.QueueLimit
                    });
            });
        });

        return services;
    }

    /// <summary>
    /// Adds the rate limiter to the middleware pipeline.
    /// Must be called after <c>UseAuthentication()</c> and <c>UseAuthorization()</c>
    /// so that <c>HttpContext.User</c> is populated when the partition key is evaluated.
    /// </summary>
    public static WebApplication UseNovaRateLimiting(this WebApplication app)
    {
        app.UseRateLimiter();
        return app;
    }
}
