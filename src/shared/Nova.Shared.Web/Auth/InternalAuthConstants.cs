namespace Nova.Shared.Web.Auth;

/// <summary>Shared constants for internal service-to-service JWT authentication.</summary>
public static class InternalAuthConstants
{
    /// <summary>
    /// JWT audience claim value for internal service tokens.
    /// Distinct from the user-facing audience (<c>nova-api</c>) so receiving services can
    /// tell internal calls apart from user requests.
    /// </summary>
    public const string Audience = "nova-internal";

    /// <summary>
    /// Named authentication scheme used for validating incoming internal service tokens.
    /// Register this via <c>AddNovaInternalAuth()</c> and apply the
    /// <see cref="PolicyName"/> policy on endpoints that accept internal calls only.
    /// </summary>
    public const string Scheme = "InternalJwt";

    /// <summary>
    /// Authorization policy name for endpoints that require a valid internal service token.
    /// Apply with <c>.RequireAuthorization(InternalAuthConstants.PolicyName)</c>.
    /// </summary>
    public const string PolicyName = "InternalService";
}
