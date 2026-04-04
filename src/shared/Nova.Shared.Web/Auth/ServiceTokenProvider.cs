using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nova.Shared.Auth;
using Nova.Shared.Configuration;
using Nova.Shared.Security;

namespace Nova.Shared.Web.Auth;

/// <summary>
/// Generates and caches short-lived JWT tokens for outbound service-to-service calls.
/// </summary>
/// <remarks>
/// Registered as a singleton — one instance per process, shared across all threads.
/// Thread-safety is guaranteed by a <see cref="SemaphoreSlim"/> around the generate-and-cache
/// path. The fast path (cached token still valid) is lock-free.
///
/// <para><b>Token contents</b></para>
/// <list type="bullet">
///   <item><c>sub</c> — the calling service name (e.g. <c>nova-shell</c>)</item>
///   <item><c>iss</c> — same issuer as user tokens (<c>AppSettings.Jwt.Issuer</c>)</item>
///   <item><c>aud</c> — <c>nova-internal</c> (distinct from user audience <c>nova-api</c>)</item>
///   <item><c>jti</c> — unique ID per token (UUID without hyphens)</item>
///   <item><c>iat</c>, <c>exp</c> — issued-at and expiry timestamps</item>
/// </list>
///
/// <para><b>Signing key</b></para>
/// <c>AppSettings.InternalAuth.SecretKey</c> is encrypted in <c>appsettings.json</c>
/// using <see cref="ICipherService"/> — the same encryption used for DB connection strings.
/// The internal signing key is deliberately separate from the user JWT key so a leaked
/// internal token cannot be used to impersonate a user.
/// </remarks>
internal sealed class ServiceTokenProvider : IServiceTokenProvider
{
    private const int ExpiryBufferSeconds = 30;

    private readonly IOptions<AppSettings>       _appOptions;
    private readonly ICipherService              _cipher;
    private readonly ILogger<ServiceTokenProvider> _logger;

    // Cached token state — updated under _lock
    private volatile string?   _cachedToken;
    private          DateTimeOffset _renewAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim  _lock    = new(1, 1);

    public ServiceTokenProvider(
        IOptions<AppSettings>        appOptions,
        ICipherService               cipher,
        ILogger<ServiceTokenProvider> logger)
    {
        _appOptions = appOptions;
        _cipher     = cipher;
        _logger     = logger;
    }

    /// <inheritdoc/>
    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        // Fast path — return cached token without acquiring the semaphore.
        // _cachedToken is volatile so the read is safe across threads.
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _renewAt)
            return _cachedToken;

        // Slow path — generate a new token under the lock.
        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring (another thread may have regenerated already).
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _renewAt)
                return _cachedToken;

            (_cachedToken, DateTimeOffset expiry) = GenerateToken();
            // Renew ExpiryBufferSeconds before actual expiry so tokens never arrive already expired.
            _renewAt = expiry.AddSeconds(-ExpiryBufferSeconds);

            _logger.LogDebug(
                "Internal service token generated for {ServiceName} (expires: {Expiry})",
                _appOptions.Value.InternalAuth.ServiceName, expiry);

            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ---------------------------------------------------------------------------

    private (string Token, DateTimeOffset Expiry) GenerateToken()
    {
        AppSettings      settings = _appOptions.Value;
        InternalAuthSettings auth = settings.InternalAuth;

        string signingKey = _cipher.Decrypt(auth.SecretKey);
        byte[] keyBytes   = Encoding.UTF8.GetBytes(signingKey);

        DateTimeOffset now    = DateTimeOffset.UtcNow;
        DateTimeOffset expiry = now.AddSeconds(auth.TokenLifetimeSeconds);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, auth.ServiceName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
            Issuer             = settings.Jwt.Issuer,
            Audience           = InternalAuthConstants.Audience,
            IssuedAt           = now.UtcDateTime,
            Expires            = expiry.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(keyBytes),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var handler = new JwtSecurityTokenHandler();
        SecurityToken token = handler.CreateToken(tokenDescriptor);
        return (handler.WriteToken(token), expiry);
    }
}
