using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Logging;
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

        // Use named options to inject IOptions<AppSettings>, ICipherService, and ILoggerFactory.
        builder.Services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AppSettings>, ICipherService, ILoggerFactory>((options, appOptions, cipher, logFactory) =>
            {
                var log = logFactory.CreateLogger("Nova.Auth.Jwt");
                AppSettings settings = appOptions.Value;
                string signingKey = cipher.Decrypt(settings.Jwt.SecretKey);

                // Startup diagnostic — shows first 6 chars so you can verify the decrypted key
                // matches your Postman jwt_secret without logging the full secret.
                log.LogInformation("[JWT] Signing key loaded. Prefix={Prefix} Len={Len}",
                    signingKey.Length >= 6 ? signingKey[..6] : signingKey, signingKey.Length);

                // Enable full exception detail in logs — remove once auth is confirmed working.
                IdentityModelEventSource.ShowPII = true;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidateAudience         = true,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer              = settings.Jwt.Issuer,
                    ValidAudience            = settings.Jwt.Audience,
                    IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
                        if (string.IsNullOrEmpty(auth))
                            log.LogWarning("[JWT] No Authorization header — {Method} {Path}",
                                ctx.Request.Method, ctx.Request.Path);
                        else
                            log.LogDebug("[JWT] Token received — {Method} {Path}",
                                ctx.Request.Method, ctx.Request.Path);
                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = ctx =>
                    {
                        log.LogWarning("[JWT] Auth failed — {Method} {Path} — {Error}: {Message}",
                            ctx.Request.Method, ctx.Request.Path,
                            ctx.Exception.GetType().Name, ctx.Exception.Message);
                        return Task.CompletedTask;
                    },
                    OnChallenge = ctx =>
                    {
                        if (!string.IsNullOrEmpty(ctx.ErrorDescription))
                            log.LogWarning("[JWT] Challenge — {Method} {Path} — {Desc}",
                                ctx.Request.Method, ctx.Request.Path, ctx.ErrorDescription);
                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddAuthorization();

        return builder;
    }
}
