using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;

namespace Nova.Shell.Api.Endpoints;

// ---------------------------------------------------------------------------
// REFERENCE ENDPOINT — delete this file when creating a real domain service.
//
// Demonstrates the patterns every Nova POST/PATCH endpoint must follow:
//
//  1. RequireAuthorization() — any endpoint injecting TenantContext must be authenticated.
//     TenantContext is resolved from the JWT by TenantResolutionMiddleware.
//     Without a valid JWT the DI factory throws → 500. Always require auth.
//  2. Request record inherits RequestContext — all 7 standard fields included.
//  3. Validation order (always in this sequence):
//       a. RequestContextValidator.Validate()      — standard fields (400 if fails)
//       b. RequestContextValidator.TenantMatches() — JWT vs body (403 if fails)
//       c. Domain-specific field validation        — business rules (400/422)
//  4. snake_case JSON binding is automatic — no [JsonPropertyName] attributes.
//  5. Use TypedResults (not Results) for all returns.
//  6. Unhandled exceptions → UseNovaProblemDetails → 500, no stack trace.
//  7. Private sealed record for request and response — one file per endpoint.
// ---------------------------------------------------------------------------

/// <summary>
/// Reference endpoint: <c>POST /echo</c>.
/// Shows <see cref="RequestContext"/> inheritance, validation convention, and Problem Details.
/// </summary>
public static class EchoEndpoint
{
    /// <summary>Registers the endpoint on the versioned route group.</summary>
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/echo", Handle)
             .RequireAuthorization()   // TenantContext requires a valid JWT — send Bearer {{access_token}}
             .WithName("Echo");
    }

    private static IResult Handle(
        EchoRequest request,
        TenantContext tenantContext,
        IHttpContextAccessor httpContextAccessor)
    {
        // Step 1 — Validate standard context fields (400 if any required field is missing).
        // Always first — ensures tenant_id, company_code, branch_code, user_id are present
        // before doing anything else.
        Dictionary<string, string[]> contextErrors = RequestContextValidator.Validate(request);
        if (contextErrors.Count > 0)
            return TypedResults.ValidationProblem(contextErrors, title: "Validation failed");

        // Step 2 — Tenant mismatch check (403 if body tenant_id ≠ JWT tenant).
        // Always second — after context fields are confirmed present.
        // Prevents cross-tenant data access via tampered or replayed requests.
        if (!RequestContextValidator.TenantMatches(request, tenantContext))
        {
            return TypedResults.Problem(
                title: "Forbidden",
                detail: "tenant_id in the request body does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Step 3 — Domain-specific field validation (400 for missing/invalid fields).
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return TypedResults.ValidationProblem(
                errors: new Dictionary<string, string[]>
                {
                    ["message"] = ["message is required."]
                },
                title: "Validation failed");
        }

        // Step 4 — Business rule violations (422 Unprocessable Entity).
        // Use 422 when the input is syntactically valid but breaks a domain rule.
        if (request.Message.Equals("not-found", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(
                title: "Resource not found",
                detail: "No resource matches the supplied identifier.",
                statusCode: StatusCodes.Status404NotFound);
        }

        // Step 5 — Prove unhandled exceptions become 500 Problem Details (no stack trace).
        if (request.Message.Equals("throw", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Deliberate exception — Problem Details handler test.");

        // Step 6 — Success path.
        // Response record properties serialise to snake_case on the wire:
        //   { "echo": "...", "received_at": "...", "correlation_id": "..." }
        string correlationId = httpContextAccessor.HttpContext?.Items["X-Correlation-ID"] as string
                               ?? string.Empty;

        return TypedResults.Ok(new EchoResponse(
            Echo: request.Message,
            ReceivedAt: DateTimeOffset.UtcNow,
            CorrelationId: correlationId));
    }

    // ---------------------------------------------------------------------------
    // Request — inherits all 7 standard context fields from RequestContext.
    // Wire format (snake_case):
    //   {
    //     "tenant_id": "...", "company_code": "...", "branch_code": "...",
    //     "user_id": "...", "browser_locale": "en-GB", "browser_timezone": "Europe/London",
    //     "ip_address": "...",   ← optional
    //     "message": "..."       ← domain-specific field
    //   }
    // ---------------------------------------------------------------------------
    private sealed record EchoRequest : RequestContext
    {
        public string Message { get; init; } = string.Empty;
    }

    // Response — wire format (snake_case):
    //   { "echo": "...", "received_at": "...", "correlation_id": "..." }
    private sealed record EchoResponse(
        string Echo,
        DateTimeOffset ReceivedAt,
        string CorrelationId);
}
