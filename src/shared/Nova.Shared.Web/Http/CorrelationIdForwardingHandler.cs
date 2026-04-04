using Microsoft.AspNetCore.Http;

namespace Nova.Shared.Web.Http;

/// <summary>
/// Propagates the current request's <c>X-Correlation-ID</c> to outbound HTTP calls.
/// Registered automatically by <see cref="HttpClientExtensions.AddNovaHttpClient"/>.
/// </summary>
/// <remarks>
/// Reads the correlation ID from <c>HttpContext.Items["X-Correlation-ID"]</c> — set by
/// <c>CorrelationIdMiddleware</c> for every inbound request. If no context is available
/// (e.g. background jobs) the header is not added and the call proceeds normally.
/// </remarks>
internal sealed class CorrelationIdForwardingHandler : DelegatingHandler
{
    private const string CorrelationIdHeader = "X-Correlation-ID";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdForwardingHandler(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_httpContextAccessor.HttpContext?.Items[CorrelationIdHeader] is string correlationId
            && !string.IsNullOrEmpty(correlationId))
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdHeader, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
