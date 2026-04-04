using Microsoft.AspNetCore.Mvc;
using Nova.Shared.Auth;
using Nova.Shared.Web.Auth;

namespace Nova.Shell.Api.Endpoints;

/// <summary>
/// Diagnostic endpoints for service-to-service (internal) JWT authentication.
///
/// ── What this tests ────────────────────────────────────────────────────────
///
///  GET /test-internal-auth/token
///      Returns the current outbound service token (for inspection / debugging).
///      Does NOT require auth — call this to see the raw JWT this service would
///      attach to outbound internal calls.
///
///  GET /test-internal-auth/protected
///      Requires a valid InternalJwt bearer token (InternalService policy).
///      Returns 200 when called by another Nova service with a valid token.
///      Returns 401/403 for user JWTs, anonymous, or invalid tokens.
///
///  GET /test-internal-auth/call-self
///      Calls /test-internal-auth/protected on itself via the internal HTTP client
///      to demonstrate the full round-trip: generate token → attach → validate.
///
/// ── How to test manually ───────────────────────────────────────────────────
///
///  1. Get the token this service generates:
///       GET /test-internal-auth/token
///     Copy the "token" value from the response.
///
///  2. Decode it at https://jwt.io — verify:
///       sub  = "nova-shell"  (ServiceName from appsettings)
///       aud  = "nova-internal"
///       iss  = "https://auth.nova.internal"
///
///  3. Call the protected endpoint with the token:
///       GET /test-internal-auth/protected
///       Authorization: Bearer <token>
///     Expected: 200 {"message": "internal call accepted", ...}
///
///  4. Call without a token:
///       GET /test-internal-auth/protected
///     Expected: 401 Unauthorized
///
///  5. Call with a user JWT (from /api/v1/echo):
///       GET /test-internal-auth/protected
///       Authorization: Bearer <user-jwt>
///     Expected: 403 Forbidden (wrong audience — user tokens have aud=nova-api)
///
///  6. Test the full round-trip (service calls itself):
///       GET /test-internal-auth/call-self
///     Expected: 200 {"message": "internal call accepted", ...} proxied back
///
/// ── Remove before production ───────────────────────────────────────────────
/// These endpoints expose a raw JWT and are for development only.
/// </summary>
public static class TestInternalAuthEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // Returns the raw outbound token for inspection — unauthenticated diagnostic.
        app.MapGet("/test-internal-auth/token", GetToken)
           .WithName("TestInternalAuthToken")
           .WithTags("_dev")
           .WithSummary("Returns the current outbound internal service token.")
           .Produces<TokenResponse>(StatusCodes.Status200OK);

        // Protected endpoint — accepts only InternalJwt tokens.
        app.MapGet("/test-internal-auth/protected", GetProtected)
           .WithName("TestInternalAuthProtected")
           .WithTags("_dev")
           .WithSummary("Protected endpoint — requires InternalService policy.")
           .RequireAuthorization(InternalAuthConstants.PolicyName)
           .Produces<ProtectedResponse>(StatusCodes.Status200OK)
           .ProducesProblem(StatusCodes.Status401Unauthorized)
           .ProducesProblem(StatusCodes.Status403Forbidden);

        // Calls the protected endpoint on itself to demonstrate the full round-trip.
        app.MapGet("/test-internal-auth/call-self", CallSelf)
           .WithName("TestInternalAuthCallSelf")
           .WithTags("_dev")
           .WithSummary("Calls /test-internal-auth/protected on itself via internal HTTP client.")
           .Produces<object>(StatusCodes.Status200OK)
           .ProducesProblem(StatusCodes.Status502BadGateway);
    }

    // -------------------------------------------------------------------------
    // Handler: /test-internal-auth/token
    // -------------------------------------------------------------------------

    private static async Task<IResult> GetToken(IServiceTokenProvider tokenProvider)
    {
        string token = await tokenProvider.GetTokenAsync();
        return TypedResults.Ok(new TokenResponse(token));
    }

    // -------------------------------------------------------------------------
    // Handler: /test-internal-auth/protected
    // -------------------------------------------------------------------------

    private static IResult GetProtected(HttpContext context)
    {
        // At this point the request has passed InternalService policy validation.
        // We can read the service identity from the sub claim.
        string? callerService = context.User.FindFirst("sub")?.Value
                             ?? context.User.Identity?.Name;

        return TypedResults.Ok(new ProtectedResponse(
            Message:       "internal call accepted",
            CallerService: callerService ?? "unknown",
            ReceivedAt:    DateTimeOffset.UtcNow));
    }

    // -------------------------------------------------------------------------
    // Handler: /test-internal-auth/call-self
    // -------------------------------------------------------------------------

    private static async Task<IResult> CallSelf(
        IHttpClientFactory  httpClientFactory,
        IServiceTokenProvider tokenProvider,
        [FromServices] ILogger<Program> logger)
    {
        // "nova-shell" client is registered in Program.cs via AddNovaInternalHttpClient.
        // ServiceTokenHandler automatically attaches the Bearer token — no manual work needed.
        HttpClient client = httpClientFactory.CreateClient("nova-shell-internal");

        HttpResponseMessage response = await client.GetAsync("/test-internal-auth/protected");

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            logger.LogWarning(
                "Call-self failed: {StatusCode} — {Body}",
                (int)response.StatusCode, body);

            return Results.Problem(
                title:      "Internal call failed",
                detail:     $"Protected endpoint returned {(int)response.StatusCode}",
                statusCode: StatusCodes.Status502BadGateway);
        }

        string json = await response.Content.ReadAsStringAsync();
        return Results.Content(json, "application/json");
    }

    // -------------------------------------------------------------------------
    // Response shapes
    // -------------------------------------------------------------------------

    private sealed record TokenResponse(string Token);

    private sealed record ProtectedResponse(
        string        Message,
        string        CallerService,
        DateTimeOffset ReceivedAt);
}
