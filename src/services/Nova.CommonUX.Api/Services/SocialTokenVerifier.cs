using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nova.CommonUX.Api.Services;

/// <summary>
/// Verifies OAuth tokens against provider endpoints.
/// Google: POST to tokeninfo endpoint.
/// Microsoft / Apple: TODO — production implementations require provider-specific SDKs
/// and JWT verification against provider public keys.
/// </summary>
internal sealed class SocialTokenVerifier : ISocialTokenVerifier
{
    private readonly IHttpClientFactory          _httpFactory;
    private readonly ILogger<SocialTokenVerifier> _logger;

    public SocialTokenVerifier(IHttpClientFactory httpFactory, ILogger<SocialTokenVerifier> logger)
    {
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    public async Task<SocialIdentity?> VerifyAsync(string provider, string socialToken, CancellationToken ct = default)
    {
        return provider.ToLowerInvariant() switch
        {
            "google"    => await VerifyGoogleAsync(socialToken, ct),
            "microsoft" => await VerifyMicrosoftAsync(socialToken, ct),
            "apple"     => await VerifyAppleAsync(socialToken, ct),
            _           => null
        };
    }

    // Google: tokeninfo endpoint accepts an ID token and returns claims as JSON.
    private async Task<SocialIdentity?> VerifyGoogleAsync(string idToken, CancellationToken ct)
    {
        try
        {
            using HttpClient client = _httpFactory.CreateClient();
            HttpResponseMessage response = await client.GetAsync(
                $"https://oauth2.googleapis.com/tokeninfo?id_token={Uri.EscapeDataString(idToken)}", ct);

            if (!response.IsSuccessStatusCode) return null;

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            JsonElement root       = doc.RootElement;

            string? sub   = root.TryGetProperty("sub",   out JsonElement s) ? s.GetString() : null;
            string? email = root.TryGetProperty("email", out JsonElement e) ? e.GetString() : null;

            if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(email)) return null;
            return new SocialIdentity(sub, email);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google token verification failed");
            return null;
        }
    }

    // TODO: Implement Microsoft token verification using Microsoft.Identity.Client or MSAL.
    // Verify JWT against Microsoft's OIDC discovery endpoint.
    private Task<SocialIdentity?> VerifyMicrosoftAsync(string token, CancellationToken ct)
    {
        _logger.LogWarning("Microsoft social token verification not yet implemented");
        return Task.FromResult<SocialIdentity?>(null);
    }

    // TODO: Implement Apple token verification using Apple's public keys.
    // Parse Sign in with Apple identity token (JWT) and verify signature.
    private Task<SocialIdentity?> VerifyAppleAsync(string token, CancellationToken ct)
    {
        _logger.LogWarning("Apple social token verification not yet implemented");
        return Task.FromResult<SocialIdentity?>(null);
    }
}
