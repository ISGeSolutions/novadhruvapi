using System.Diagnostics;

namespace Nova.Shell.Api.Endpoints;

// ---------------------------------------------------------------------------
// REFERENCE ENDPOINT — delete this file when creating a real domain service.
//
// Demonstrates the pattern for making resilient outbound HTTP calls:
//
//  1. Register the named client in Program.cs:
//       builder.Services.AddNovaHttpClient("nova-auth",
//           builder.Configuration["Services:NovaAuth:BaseUrl"] ?? "http://localhost:5200");
//
//  2. Inject IHttpClientFactory into the handler.
//
//  3. Create the client by name:
//       HttpClient client = httpClientFactory.CreateClient("nova-auth");
//
//  4. Call the target — the resilience pipeline (retry, circuit breaker, timeout)
//     is applied automatically. X-Correlation-ID is forwarded to the target service.
//
//  5. The resilience pipeline throws BrokenCircuitException or TimeoutRejectedException
//     when the circuit is open or the total timeout is exceeded. Let these propagate —
//     UseNovaProblemDetails will catch them and return 500. Add explicit error handling
//     only when you need a specific non-500 response (e.g. 503 with Retry-After).
// ---------------------------------------------------------------------------

/// <summary>
/// Reference endpoint: <c>GET /api/v1/http-ping</c>.
/// Demonstrates resilient outbound HTTP calls via <see cref="IHttpClientFactory"/>.
/// </summary>
public static class HttpPingEndpoint
{
    /// <summary>Registers the endpoint on the versioned route group.</summary>
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/http-ping", Handle)
             .AllowAnonymous()
             .WithName("HttpPing");
    }

    private static async Task<IResult> Handle(IHttpClientFactory httpClientFactory)
    {
        // Retrieve the named client registered by AddNovaHttpClient() in Program.cs.
        // The client already has:
        //   - BaseAddress set to Services:NovaShell:BaseUrl
        //   - Accept: application/json header
        //   - CorrelationIdForwardingHandler — X-Correlation-ID propagated to the target
        //   - Standard resilience pipeline (retry, circuit breaker, timeouts)
        HttpClient client = httpClientFactory.CreateClient("nova-shell");

        Stopwatch sw = Stopwatch.StartNew();

        // The resilience pipeline is transparent — call the target as normal.
        // Retries, circuit breaker trips, and timeouts are handled by the pipeline.
        // If the total timeout (30 s) is exceeded or the circuit is open,
        // an exception propagates and UseNovaProblemDetails returns 500.
        HttpResponseMessage response = await client.GetAsync("/health/redis");

        sw.Stop();

        return TypedResults.Ok(new HttpPingResponse(
            Target:    (client.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty) + "/health/redis",
            Status:    (int)response.StatusCode,
            IsSuccess: response.IsSuccessStatusCode,
            LatencyMs: sw.ElapsedMilliseconds));
    }

    // Response — wire format:
    //   { "target": "http://localhost:5100/health", "status": 200, "is_success": true, "latency_ms": 12 }
    private sealed record HttpPingResponse(
        string Target,
        int    Status,
        bool   IsSuccess,
        long   LatencyMs);
}
