using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nova.Shared.Auth;
using Nova.Shared.Configuration;
using Nova.Shared.Security;

namespace Nova.Shared.Web.Auth;

/// <summary>
/// Extension methods for configuring service-to-service JWT authentication.
/// </summary>
public static class InternalAuthExtensions
{
    /// <summary>
    /// Configures service-to-service authentication on this service:
    /// <list type="bullet">
    ///   <item>
    ///     Registers the <c>InternalJwt</c> authentication scheme — validates tokens whose
    ///     audience is <c>nova-internal</c> using the key from
    ///     <c>appsettings.json → InternalAuth.SecretKey</c> (encrypted).
    ///   </item>
    ///   <item>
    ///     Registers the <c>InternalService</c> authorization policy — apply to endpoints
    ///     that should only be callable by other Nova services.
    ///   </item>
    ///   <item>
    ///     Registers <see cref="IServiceTokenProvider"/> — call
    ///     <c>AddNovaInternalHttpClient</c> to automatically attach the token to outbound calls.
    ///   </item>
    /// </list>
    ///
    /// Call this after <c>AddNovaJwt()</c> in <c>Program.cs</c>.
    /// </summary>
    public static WebApplicationBuilder AddNovaInternalAuth(this WebApplicationBuilder builder)
    {
        // Add the InternalJwt scheme alongside the existing user JWT scheme.
        // Each scheme validates a different audience — they do not interfere with each other.
        builder.Services
            .AddAuthentication()
            .AddJwtBearer(InternalAuthConstants.Scheme, _ => { });

        // Configure the scheme options using the decrypted InternalAuth.SecretKey.
        builder.Services
            .AddOptions<JwtBearerOptions>(InternalAuthConstants.Scheme)
            .Configure<IOptions<AppSettings>, ICipherService>((options, appOptions, cipher) =>
            {
                AppSettings      settings = appOptions.Value;
                InternalAuthSettings auth = settings.InternalAuth;

                string signingKey = cipher.Decrypt(auth.SecretKey);

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidateAudience         = true,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer              = settings.Jwt.Issuer,
                    ValidAudience            = InternalAuthConstants.Audience,
                    IssuerSigningKey         = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(signingKey))
                };
            });

        // InternalService policy — requires authentication via the InternalJwt scheme only.
        // Endpoints decorated with this policy reject user JWTs and anonymous requests.
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(InternalAuthConstants.PolicyName, policy =>
                policy.AddAuthenticationSchemes(InternalAuthConstants.Scheme)
                      .RequireAuthenticatedUser());
        });

        // IServiceTokenProvider — generates and caches outbound service tokens.
        // Singleton: token generation and caching are thread-safe.
        builder.Services.AddSingleton<IServiceTokenProvider, ServiceTokenProvider>();

        return builder;
    }
}
