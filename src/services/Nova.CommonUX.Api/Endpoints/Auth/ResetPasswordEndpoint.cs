using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.CommonUX.Api.Models;
using Nova.CommonUX.Api.Services;
using Nova.Shared.Data;

namespace Nova.CommonUX.Api.Endpoints.Auth;

/// <summary>
/// Reset password: <c>POST /api/v1/auth/reset-password</c>
/// Validates the reset token (SHA-256 hash lookup), updates password_hash, marks token used,
/// and revokes all refresh tokens for the user.
/// </summary>
public static class ResetPasswordEndpoint
{
    // Min 8 chars, at least 1 upper, 1 lower, 1 digit
    private static readonly Regex PasswordPolicy =
        new(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$", RegexOptions.Compiled);

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/auth/reset-password", HandleAsync)
             .AllowAnonymous()
             .WithName("AuthResetPassword");
    }

    private static async Task<IResult> HandleAsync(
        ResetPasswordRequest     request,
        IDbConnectionFactory     connectionFactory,
        IOptions<AuthDbSettings> authDbOptions,
        ISessionStore            sessionStore,
        CancellationToken        ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Token))       errors["token"]        = ["token is required."];
        if (string.IsNullOrWhiteSpace(request.NewPassword)) errors["new_password"] = ["new_password is required."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        if (!PasswordPolicy.IsMatch(request.NewPassword!))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["new_password"] = ["Password must be at least 8 characters and contain at least one uppercase letter, one lowercase letter, and one number."]
                },
                title: "Validation failed");

        AuthDbSettings authDb     = authDbOptions.Value;
        ISqlDialect    dialect    = AuthDbHelper.Dialect(authDb.DbType);
        string         authTokens = dialect.TableRef("nova_auth", "tenant_auth_tokens");
        string         userAuth   = dialect.TableRef("nova_auth", "tenant_user_auth");

        string tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token!)));
        var    now       = AuthDbHelper.UtcNow();

        using IDbConnection connection = connectionFactory.CreateFromConnectionString(
            authDb.ConnectionString, authDb.DbType);

        TokenRow? tokenRow = await connection.QuerySingleOrDefaultAsync<TokenRow>(
            $"""
            SELECT id, tenant_id, user_id, expires_on, used_on
            FROM {authTokens}
            WHERE token_hash = @TokenHash AND token_type = 'password_reset'
            """,
            new { TokenHash = tokenHash },
            commandTimeout: 10);

        if (tokenRow is null || tokenRow.UsedOn.HasValue || tokenRow.ExpiresOn <= now)
            return TypedResults.Problem(
                title:      "Unauthorized",
                detail:     "Invalid or expired reset token.",
                statusCode: StatusCodes.Status401Unauthorized);

        string newHash = Argon2idHasher.Hash(request.NewPassword!);

        // Mark token as used
        await connection.ExecuteAsync(
            $"UPDATE {authTokens} SET used_on = @Now WHERE id = @Id",
            new { Now = now, tokenRow.Id },
            commandTimeout: 10);

        // Update password hash
        await connection.ExecuteAsync(
            $"""
            UPDATE {userAuth}
            SET password_hash = @Hash,
                updated_on    = @Now,
                updated_by    = 'pwd-reset',
                updated_at    = 'Nova.CommonUX.Api'
            WHERE tenant_id = @TenantId AND user_id = @UserId
            """,
            new { Hash = newHash, Now = now, TenantId = tokenRow.TenantId, UserId = tokenRow.UserId },
            commandTimeout: 10);

        // Revoke all refresh tokens for this user — password reset is a security event
        await sessionStore.DeleteByPatternAsync($"refresh:{tokenRow.TenantId}:{tokenRow.UserId}:*", ct);

        return TypedResults.Ok(new MessageResponse("Password has been reset successfully."));
    }

    private sealed record TokenRow
    {
        public Guid            Id        { get; set; }
        public string          TenantId  { get; set; } = string.Empty;
        public string          UserId    { get; set; } = string.Empty;
        public DateTimeOffset  ExpiresOn { get; set; }
        public DateTimeOffset? UsedOn    { get; set; }
    }

    private sealed record ResetPasswordRequest
    {
        public string? Token       { get; set; }
        public string? NewPassword { get; set; }
    }
}
