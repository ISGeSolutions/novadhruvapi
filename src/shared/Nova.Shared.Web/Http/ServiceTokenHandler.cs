using System.Net.Http.Headers;
using Nova.Shared.Auth;

namespace Nova.Shared.Web.Http;

/// <summary>
/// Delegating handler that attaches a service-to-service Bearer token to every outbound request
/// made via an internal <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// Registered automatically by <c>AddNovaInternalHttpClient()</c> — do not add this handler
/// manually to regular (<c>AddNovaHttpClient</c>) clients.
///
/// <para><b>How it works</b></para>
/// Before each request is sent, the handler calls <see cref="IServiceTokenProvider.GetTokenAsync"/>
/// which returns a cached token (fast path, no crypto) or generates a new one when the cached
/// token is within 30 seconds of expiry (slow path, under a semaphore).
/// The token is then set as the <c>Authorization: Bearer</c> header.
///
/// <para><b>Token type</b></para>
/// The token identifies the <em>calling service</em> (not an end user). The receiving service
/// validates it against the <c>InternalJwt</c> scheme and the <c>InternalService</c> policy.
/// </remarks>
internal sealed class ServiceTokenHandler : DelegatingHandler
{
    private readonly IServiceTokenProvider _tokenProvider;

    public ServiceTokenHandler(IServiceTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string token = await _tokenProvider.GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
