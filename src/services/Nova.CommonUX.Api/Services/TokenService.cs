using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nova.Shared.Configuration;
using Nova.Shared.Security;

namespace Nova.CommonUX.Api.Services;

/// <summary>
/// Issues signed JWTs and opaque refresh tokens.
/// Uses the same <c>Jwt</c> signing key as all other Nova services so tokens
/// are accepted by downstream services without additional configuration.
/// </summary>
public sealed class TokenService : ITokenService
{
    private const int JwtExpirySeconds = 3600;

    private readonly IOptions<AppSettings> _appOptions;
    private readonly ICipherService        _cipher;

    public TokenService(IOptions<AppSettings> appOptions, ICipherService cipher)
    {
        _appOptions = appOptions;
        _cipher     = cipher;
    }

    /// <inheritdoc/>
    public (string Token, int ExpiresIn) IssueJwt(string tenantId, string userId, IEnumerable<string> roles)
    {
        AppSettings settings   = _appOptions.Value;
        string      signingKey = _cipher.Decrypt(settings.Jwt.SecretKey);

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new("tenant_id", tenantId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        foreach (string role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var token = new JwtSecurityToken(
            issuer:             settings.Jwt.Issuer,
            audience:           settings.Jwt.Audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddSeconds(JwtExpirySeconds),
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), JwtExpirySeconds);
    }

    /// <inheritdoc/>
    public string GenerateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}
