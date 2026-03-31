using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nova.Shared.Configuration;
using Nova.Shared.Security;

namespace Nova.Shared.Web.Auth;

/// <summary>Extension methods for configuring JWT bearer authentication.</summary>
public static class JwtSetupExtensions
{
    /// <summary>
    /// Adds JWT bearer authentication, decrypting the signing key via <see cref="ICipherService"/>.
    /// </summary>
    public static WebApplicationBuilder AddNovaJwt(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Use named options to inject IOptions<AppSettings> and ICipherService at configuration time.
        builder.Services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AppSettings>, ICipherService>((options, appOptions, cipher) =>
            {
                AppSettings settings = appOptions.Value;
                string signingKey = cipher.Decrypt(settings.Jwt.SecretKey);

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = settings.Jwt.Issuer,
                    ValidAudience = settings.Jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
                };
            });

        builder.Services.AddAuthorization();

        return builder;
    }
}
