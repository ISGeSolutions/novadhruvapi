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
/// Forgot password: <c>POST /api/v1/auth/forgot-password</c>
/// Sends a password reset email. Always returns 200 regardless of whether the user exists
/// (security by obscurity — no user enumeration).
/// </summary>
public static class ForgotPasswordEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/auth/forgot-password", HandleAsync)
             .AllowAnonymous()
             .WithName("AuthForgotPassword");
    }

    private static async Task<IResult> HandleAsync(
        ForgotPasswordRequest         request,
        IDbConnectionFactory          connectionFactory,
        IOptions<AuthDbSettings>      authDbOptions,
        IOptionsMonitor<AuthSettings> authMonitor,
        IEmailSender                  emailSender,
        CancellationToken             ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.TenantId)) errors["tenant_id"] = ["tenant_id is required."];
        if (string.IsNullOrWhiteSpace(request.UserId))   errors["user_id"]   = ["user_id is required."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        AuthDbSettings authDb      = authDbOptions.Value;
        ISqlDialect    dialect     = AuthDbHelper.Dialect(authDb.DbType);
        string         userProfile = dialect.TableRef("nova_auth", "tenant_user_profile");
        string         authTokens  = dialect.TableRef("nova_auth", "tenant_auth_tokens");

        using IDbConnection connection = connectionFactory.CreateFromConnectionString(
            authDb.ConnectionString, authDb.DbType);

        string? email = await connection.ExecuteScalarAsync<string?>(
            $"""
            SELECT email FROM {userProfile}
            WHERE tenant_id = @TenantId AND user_id = @UserId AND frz_ind = {dialect.BooleanLiteral(false)}
            """,
            new { request.TenantId, request.UserId },
            commandTimeout: 10);

        // Always return success — do not reveal if the user exists
        if (string.IsNullOrEmpty(email))
            return Ok();

        // Generate token, hash, store, send email
        string plainToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        string tokenHash  = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainToken)));
        var    now        = AuthDbHelper.UtcNow();
        var    expiresOn  = now.AddMinutes(authMonitor.CurrentValue.PasswordResetTokenExpiryMinutes);

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {authTokens} (id, tenant_id, user_id, token_hash, token_type, expires_on, used_on, created_on)
            VALUES (@Id, @TenantId, @UserId, @TokenHash, 'password_reset', @ExpiresOn, NULL, @Now)
            """,
            new
            {
                Id        = Guid.NewGuid(),
                request.TenantId,
                request.UserId,
                TokenHash = tokenHash,
                ExpiresOn = expiresOn,
                Now       = now,
            },
            commandTimeout: 10);

        await emailSender.SendAsync(
            to:            email,
            subject:       "Nova — Password Reset",
            plainTextBody: $"Click the link to reset your password: https://app.nova.internal/reset-password?token={Uri.EscapeDataString(plainToken)}",
            ct:            ct);

        return Ok();
    }

    private static IResult Ok() =>
        TypedResults.Ok(new MessageResponse("If this user exists, a reset email has been sent."));

    private sealed record ForgotPasswordRequest
    {
        public string TenantId { get; init; } = string.Empty;
        public string UserId   { get; init; } = string.Empty;
    }
}
