using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Nova.Shell.Api.Tests.Helpers;

/// <summary>
/// Generates signed JWT tokens for use in test HTTP requests.
/// All parameters are explicit — no configuration files or environment variables are read.
/// </summary>
public static class JwtFactory
{
    /// <summary>
    /// Creates a signed JWT bearer token with the given claims.
    /// Token lifetime is 1 hour from the moment of creation.
    /// </summary>
    /// <param name="tenantId">The tenant_id claim value.</param>
    /// <param name="userId">The sub (subject / user ID) claim value.</param>
    /// <param name="secret">The HMAC-SHA256 signing secret (plaintext, minimum 32 chars).</param>
    /// <param name="issuer">The iss claim value.</param>
    /// <param name="audience">The aud claim value.</param>
    public static string CreateToken(
        string tenantId,
        string userId,
        string secret,
        string issuer,
        string audience)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("tenant_id", tenantId),
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
