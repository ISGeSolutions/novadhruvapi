namespace Nova.CommonUX.Api.Services;

/// <summary>Verifies a social provider OAuth token and extracts the user's provider identity.</summary>
public interface ISocialTokenVerifier
{
    /// <summary>
    /// Verifies <paramref name="socialToken"/> with the named <paramref name="provider"/>
    /// and returns the provider-issued user identity, or <c>null</c> if the token is invalid.
    /// </summary>
    /// <param name="provider">One of: <c>google</c>, <c>microsoft</c>, <c>apple</c>.</param>
    /// <param name="socialToken">OAuth token or ID token from the OAuth callback.</param>
    Task<SocialIdentity?> VerifyAsync(string provider, string socialToken, CancellationToken ct = default);
}

/// <summary>Identity returned by a social provider after token verification.</summary>
public sealed record SocialIdentity(string ProviderId, string Email);
