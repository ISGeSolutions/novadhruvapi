namespace Nova.CommonUX.Api.Services;

/// <summary>Issues and validates JWT tokens for the Nova platform.</summary>
public interface ITokenService
{
    /// <summary>
    /// Issues a signed JWT for the given user.
    /// </summary>
    /// <param name="tenantId">Tenant the user belongs to.</param>
    /// <param name="userId">Authenticated user identifier.</param>
    /// <param name="roles">Role codes to embed as claims.</param>
    /// <returns>Signed JWT string and its expiry duration in seconds.</returns>
    (string Token, int ExpiresIn) IssueJwt(string tenantId, string userId, IEnumerable<string> roles);

    /// <summary>
    /// Generates a cryptographically random opaque refresh token.
    /// </summary>
    string GenerateRefreshToken();
}
