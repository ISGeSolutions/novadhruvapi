using Nova.Shared.Requests;

namespace Nova.Shared.Validation;

/// <summary>
/// Validates the pagination fields on a <see cref="PagedRequest"/>.
/// </summary>
/// <remarks>
/// Call this after <see cref="RequestContextValidator.Validate"/> in the validation sequence:
/// <code>
/// // Step 1 — standard context fields
/// var contextErrors = RequestContextValidator.Validate(request);
/// if (contextErrors.Count > 0)
///     return TypedResults.ValidationProblem(contextErrors, title: "Validation failed");
///
/// // Step 2 — tenant mismatch
/// if (!RequestContextValidator.TenantMatches(request, tenantContext))
///     return TypedResults.Problem(title: "Forbidden", statusCode: 403);
///
/// // Step 3 — pagination fields
/// var pageErrors = PagedRequestValidator.Validate(request);
/// if (pageErrors.Count > 0)
///     return TypedResults.ValidationProblem(pageErrors, title: "Validation failed");
/// </code>
/// </remarks>
public static class PagedRequestValidator
{
    /// <summary>Maximum permitted value for <c>page_size</c>.</summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// Validates <see cref="PagedRequest.PageNumber"/> and <see cref="PagedRequest.PageSize"/>.
    /// Returns an empty dictionary when both fields are valid.
    /// </summary>
    public static Dictionary<string, string[]> Validate(PagedRequest request)
    {
        Dictionary<string, string[]> errors = new();

        if (request.PageNumber < 1)
            errors["page_number"] = ["page_number must be 1 or greater."];

        if (request.PageSize < 1 || request.PageSize > MaxPageSize)
            errors["page_size"] = [$"page_size must be between 1 and {MaxPageSize}."];

        return errors;
    }
}
