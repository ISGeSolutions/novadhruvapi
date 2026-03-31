using Microsoft.AspNetCore.Http;

namespace Nova.Shared.Web.Middleware;

/// <summary>
/// Reads the <c>X-Correlation-ID</c> request header (or generates a new one) and
/// stores it in <c>HttpContext.Items</c> for use by downstream components.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    /// <summary>Initialises the middleware.</summary>
    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Processes the request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
                               ?? Guid.CreateVersion7().ToString();

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        await _next(context);
    }
}
