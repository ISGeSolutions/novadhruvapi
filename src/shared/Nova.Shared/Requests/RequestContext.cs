namespace Nova.Shared.Requests;

/// <summary>
/// Base record for all POST and PATCH request bodies in Nova APIs.
/// </summary>
/// <remarks>
/// The frontend <c>apiClient</c> automatically injects these seven fields on every
/// POST and PATCH request. All domain request records inherit from this type so that
/// the standard context is always present and validated consistently.
///
/// Inheriting a domain request record:
/// <code>
/// public sealed record SaveBookingRequest : RequestContext
/// {
///     public required string Reference { get; init; }
///     public required DateOnly BookingDate { get; init; }
/// }
/// </code>
///
/// Wire format: all properties serialise to snake_case (e.g. <c>tenant_id</c>).
/// Incoming JSON is bound case-insensitively — camelCase and snake_case both work.
/// </remarks>
public record RequestContext
{
    /// <summary>
    /// The tenant that owns this request. Must be validated against the JWT-resolved
    /// <c>TenantContext.TenantId</c> — a mismatch must return HTTP 403.
    /// </summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>The company within the tenant that this request is scoped to.</summary>
    public string CompanyCode { get; init; } = string.Empty;

    /// <summary>The branch within the company that this request is scoped to.</summary>
    public string BranchCode { get; init; } = string.Empty;

    /// <summary>The ID of the authenticated user making this request.</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// The user's browser locale (e.g. <c>en-GB</c>).
    /// Stored for display preferences only — not used for business logic.
    /// </summary>
    public string BrowserLocale { get; init; } = string.Empty;

    /// <summary>
    /// The user's IANA browser timezone (e.g. <c>Europe/London</c>).
    /// Stored for display preferences only — not used for business logic.
    /// </summary>
    public string BrowserTimezone { get; init; } = string.Empty;

    /// <summary>
    /// The client IP address as reported by the browser (sourced from ipify.org on startup).
    /// May be <c>null</c> if the lookup failed. Store in the <c>updated_at</c> audit column.
    /// Do not use for security decisions — read <c>X-Forwarded-For</c> for that instead.
    /// </summary>
    public string? IpAddress { get; init; }
}
