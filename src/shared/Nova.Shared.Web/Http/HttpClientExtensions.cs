using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Auth;

namespace Nova.Shared.Web.Http;

/// <summary>
/// Extension methods for registering resilient outbound HTTP clients on Nova services.
/// </summary>
public static class HttpClientExtensions
{
    /// <summary>
    /// Registers a named <see cref="HttpClient"/> with standard resilience and correlation ID forwarding.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientName">
    /// The logical name used to retrieve the client via
    /// <c>IHttpClientFactory.CreateClient(clientName)</c>.
    /// </param>
    /// <param name="baseUrl">The base URL for all requests made by this client.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Each registered client gets:
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>CorrelationIdForwardingHandler</c> — propagates <c>X-Correlation-ID</c> from the
    ///     inbound request to every outbound call, enabling end-to-end tracing across services.
    ///   </description></item>
    ///   <item><description>
    ///     <c>AddStandardResilienceHandler()</c> — Polly-backed pipeline:
    ///     <list type="bullet">
    ///       <item><description>Total timeout: 30 s</description></item>
    ///       <item><description>Retry: up to 3 attempts, exponential back-off with jitter, on 408 / 429 / 5xx</description></item>
    ///       <item><description>Circuit breaker: opens after failures; half-open after 30 s</description></item>
    ///       <item><description>Per-attempt timeout: 10 s</description></item>
    ///     </list>
    ///   </description></item>
    /// </list>
    ///
    /// Usage in Program.cs (before <c>builder.Build()</c>):
    /// <code>
    /// builder.Services.AddNovaHttpClient("nova-auth",
    ///     builder.Configuration["Services:NovaAuth:BaseUrl"] ?? "http://localhost:5200");
    /// </code>
    ///
    /// Usage in an endpoint handler:
    /// <code>
    /// private static async Task&lt;IResult&gt; Handle(IHttpClientFactory httpClientFactory)
    /// {
    ///     HttpClient client = httpClientFactory.CreateClient("nova-auth");
    ///     HttpResponseMessage response = await client.GetAsync("/health");
    ///     return TypedResults.Ok(new { status = response.StatusCode });
    /// }
    /// </code>
    /// </remarks>
    public static IServiceCollection AddNovaHttpClient(
        this IServiceCollection services,
        string clientName,
        string baseUrl)
    {
        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdForwardingHandler>();

        services.AddHttpClient(clientName, client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        })
        .AddHttpMessageHandler<CorrelationIdForwardingHandler>()
        .AddStandardResilienceHandler();

        return services;
    }

    /// <summary>
    /// Registers a named <see cref="HttpClient"/> for calling other internal Nova services.
    /// Identical to <see cref="AddNovaHttpClient"/> but also attaches an internal Bearer token
    /// to every outbound request via <see cref="ServiceTokenHandler"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientName">
    /// The logical name used to retrieve the client via
    /// <c>IHttpClientFactory.CreateClient(clientName)</c>.
    /// </param>
    /// <param name="baseUrl">The base URL of the target internal service.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Requires <c>AddNovaInternalAuth()</c> to have been called first (registers
    /// <see cref="IServiceTokenProvider"/>). The token is generated once and cached;
    /// renewed automatically 30 s before expiry.
    ///
    /// Usage in Program.cs:
    /// <code>
    /// builder.Services.AddNovaInternalHttpClient("nova-auth",
    ///     builder.Configuration["Services:NovaAuth:BaseUrl"] ?? "http://localhost:5200");
    /// </code>
    ///
    /// Usage in an endpoint handler:
    /// <code>
    /// HttpClient client = httpClientFactory.CreateClient("nova-auth");
    /// HttpResponseMessage response = await client.GetAsync("/internal/resource");
    /// </code>
    /// </remarks>
    public static IServiceCollection AddNovaInternalHttpClient(
        this IServiceCollection services,
        string clientName,
        string baseUrl)
    {
        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdForwardingHandler>();
        services.AddTransient<ServiceTokenHandler>();

        services.AddHttpClient(clientName, client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        })
        .AddHttpMessageHandler<CorrelationIdForwardingHandler>()
        .AddHttpMessageHandler<ServiceTokenHandler>()
        .AddStandardResilienceHandler();

        return services;
    }
}
