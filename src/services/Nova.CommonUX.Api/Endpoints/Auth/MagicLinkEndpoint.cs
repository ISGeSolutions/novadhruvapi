using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.CommonUX.Api.Models;
using Nova.CommonUX.Api.Services;
using Nova.Shared.Data;

namespace Nova.CommonUX.Api.Endpoints.Auth;

/// <summary>
/// Magic link send: <c>POST /api/v1/auth/magic-link</c>
/// Looks up the user by (tenant_id, email), generates a single-use magic link token,
/// and sends it by email. Always returns 200 (security by obscurity).
/// </summary>
public static class MagicLinkEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/auth/magic-link", HandleAsync)
             .AllowAnonymous()
             .WithName("AuthMagicLink");
    }

    private static async Task<IResult> HandleAsync(
        MagicLinkRequest              request,
        IDbConnectionFactory          connectionFactory,
        IOptions<AuthDbSettings>      authDbOptions,
        IOptionsMonitor<AuthSettings> authMonitor,
        IEmailSender                  emailSender,
        CancellationToken             ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.TenantId)) errors["tenant_id"] = ["tenant_id is required."];
        if (string.IsNullOrWhiteSpace(request.Email))    errors["email"]      = ["email is required."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        AuthDbSettings authDb     = authDbOptions.Value;
        ISqlDialect    dialect    = AuthDbHelper.Dialect(authDb.DbType);
        string         profile    = dialect.TableRef("nova_auth", "tenant_user_profile");
        string         authTokens = dialect.TableRef("nova_auth", "tenant_auth_tokens");

        using IDbConnection connection = connectionFactory.CreateFromConnectionString(
            authDb.ConnectionString, authDb.DbType);

        // Look up user by (tenant_id, email)
        ProfileEmailRow? row = await connection.QuerySingleOrDefaultAsync<ProfileEmailRow>(
            $"""
            SELECT user_id, email FROM {profile}
            WHERE tenant_id = @TenantId AND email = @Email AND frz_ind = {dialect.BooleanLiteral(false)}
            """,
            new { request.TenantId, request.Email },
            commandTimeout: 10);

        // Always return success
        if (row is null)
            return Ok();

        // URL-safe base64 (no +/= chars) so the token can be used directly in URLs without URI-escaping
        string plainToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
                                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        string tokenHash  = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainToken)));
        var    now        = AuthDbHelper.UtcNow();
        var    expiresOn  = now.AddMinutes(authMonitor.CurrentValue.MagicLinkTokenExpiryMinutes);

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {authTokens} (id, tenant_id, user_id, token_hash, token_type, expires_on, used_on, created_on)
            VALUES (@Id, @TenantId, @UserId, @TokenHash, 'magic_link', @ExpiresOn, NULL, @Now)
            """,
            new
            {
                Id        = Guid.CreateVersion7(),
                request.TenantId,
                UserId    = row.UserId,
                TokenHash = tokenHash,
                ExpiresOn = expiresOn,
                Now       = now,
            },
            commandTimeout: 10);

        await emailSender.SendAsync(
            to:            row.Email,
            subject:       "Nova — Your magic link",
            plainTextBody: $"Click this link to sign in (expires in {authMonitor.CurrentValue.MagicLinkTokenExpiryMinutes} minutes): https://app.nova.internal/magic?token={plainToken}",
            ct:            ct);

        return Ok();
    }

    private static IResult Ok() =>
        TypedResults.Ok(new MessageResponse("If this email is registered, a magic link has been sent."));

    private sealed record ProfileEmailRow(string UserId, string Email);

    private sealed record MagicLinkRequest
    {
        public string TenantId { get; init; } = string.Empty;
        public string Email    { get; init; } = string.Empty;
    }
}
