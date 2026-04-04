using Nova.Shared.Requests;
using Nova.Shared.Tenancy;

namespace Nova.Shared.Validation;

/// <summary>
/// Validates the standard <see cref="RequestContext"/> fields that are present on every
/// POST and PATCH request body.
/// </summary>
/// <remarks>
/// Returns a <c>Dictionary&lt;string, string[]&gt;</c> — the exact type accepted by
/// <c>TypedResults.ValidationProblem(errors)</c> — so the caller can return a 400
/// response with no extra mapping.
///
/// Usage pattern in an endpoint handler:
/// <code>
/// Dictionary&lt;string, string[]&gt; errors = RequestContextValidator.Validate(request);
/// if (errors.Count > 0)
///     return TypedResults.ValidationProblem(errors, title: "Validation failed");
///
/// if (!RequestContextValidator.TenantMatches(request, tenantContext))
///     return TypedResults.Problem(title: "Forbidden", statusCode: 403);
/// </code>
/// </remarks>
public static class RequestContextValidator
{
    /// <summary>
    /// Validates the required standard context fields on <paramref name="request"/>.
    /// Returns an empty dictionary when all required fields are present.
    /// </summary>
    /// <remarks>
    /// Fields validated as required: <c>tenant_id</c>, <c>company_id</c>,
    /// <c>branch_id</c>, <c>user_id</c>.
    /// <c>browser_locale</c>, <c>browser_timezone</c>, and <c>ip_address</c> are
    /// informational and are not required for the request to be accepted.
    /// </remarks>
    public static Dictionary<string, string[]> Validate(RequestContext request)
    {
        Dictionary<string, string[]> errors = new();

        if (string.IsNullOrWhiteSpace(request.TenantId))
            errors["tenant_id"] = ["tenant_id is required."];

        if (string.IsNullOrWhiteSpace(request.CompanyId))
            errors["company_id"] = ["company_id is required."];

        if (string.IsNullOrWhiteSpace(request.BranchId))
            errors["branch_id"] = ["branch_id is required."];

        if (string.IsNullOrWhiteSpace(request.UserId))
            errors["user_id"] = ["user_id is required."];

        return errors;
    }

    /// <summary>
    /// Returns <c>true</c> when the <c>tenant_id</c> in the request body matches the
    /// tenant resolved from the JWT by <see cref="TenantContext"/>.
    /// A mismatch must be rejected with HTTP 403 — it indicates a tampered or replayed request.
    /// </summary>
    public static bool TenantMatches(RequestContext request, TenantContext tenant)
        => string.Equals(request.TenantId, tenant.TenantId, StringComparison.Ordinal);
}
