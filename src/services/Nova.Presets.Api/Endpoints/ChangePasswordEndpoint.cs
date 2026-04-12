using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.Presets.Api.Configuration;
using Nova.Presets.Api.Services;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.Presets.Api.Endpoints;

/// <summary>
/// Initiate password change: <c>POST /api/v1/user-profile/change-password</c>
///
/// Verifies the current password against <c>nova_auth.tenant_user_auth</c>,
/// hashes the new password, stores a confirmation token in <c>presets.tenant_password_change_requests</c>,
/// and sends a confirmation email. The new password takes effect only after the user
/// confirms via <see cref="ConfirmPasswordChangeEndpoint"/>.
/// </summary>
public static class ChangePasswordEndpoint
{
    private static readonly Regex PasswordPolicy =
        new(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$", RegexOptions.Compiled);

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/user-profile/change-password", HandleAsync)
             .RequireAuthorization()
             .WithName("ChangePassword");
    }

    private static async Task<IResult> HandleAsync(
        ChangePasswordRequest                   request,
        HttpContext                             httpContext,
        IDbConnectionFactory                   connectionFactory,
        IOptions<AuthDbSettings>               authDbOptions,
        IOptions<PresetsDbSettings>            presetsDbOptions,
        IOptionsMonitor<ChangePasswordSettings> changePasswordMonitor,
        IEmailSender                            emailSender,
        IConfiguration                         configuration,
        CancellationToken                      ct)
    {
        Dictionary<string, string[]> errors = RequestContextValidator.Validate(request);

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            errors["current_password"] = ["current_password is required."];
        if (string.IsNullOrWhiteSpace(request.NewPassword))
            errors["new_password"] = ["new_password is required."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        if (!PasswordPolicy.IsMatch(request.NewPassword!))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["new_password"] = ["Password must be at least 8 characters and contain at least one uppercase letter, one lowercase letter, and one number."]
                },
                title: "Validation failed");

        string? jwtTenantId = httpContext.User.FindFirstValue("tenant_id");
        if (!string.Equals(request.TenantId, jwtTenantId, StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        AuthDbSettings    authDb    = authDbOptions.Value;
        PresetsDbSettings presetsDb = presetsDbOptions.Value;

        ISqlDialect authDialect    = PresetsDbHelper.Dialect(authDb.DbType);
        ISqlDialect presetsDialect = PresetsDbHelper.Dialect(presetsDb.DbType);

        string userAuth  = authDialect.TableRef("nova_auth", "tenant_user_auth");
        string profile   = authDialect.TableRef("nova_auth", "tenant_user_profile");
        string changeReq = presetsDialect.TableRef("presets", "tenant_password_change_requests");

        // Step 1: Verify current password
        AuthRow? authRow;
        string?  email;
        using (IDbConnection authConn = connectionFactory.CreateFromConnectionString(
                   authDb.ConnectionString, authDb.DbType))
        {
            authRow = await authConn.QuerySingleOrDefaultAsync<AuthRow>(
                $"""
                SELECT user_id AS UserId, password_hash AS PasswordHash
                FROM   {userAuth}
                WHERE  tenant_id = @TenantId AND user_id = @UserId
                AND    frz_ind   = {authDialect.BooleanLiteral(false)}
                """,
                new { request.TenantId, request.UserId },
                commandTimeout: 10);

            if (authRow is null)
                return TypedResults.Problem(
                    title:      "Not found",
                    detail:     "User not found.",
                    statusCode: StatusCodes.Status404NotFound);

            if (authRow.PasswordHash is null || !Argon2idHasher.Verify(request.CurrentPassword!, authRow.PasswordHash))
                return TypedResults.Problem(
                    title:      "Unauthorized",
                    detail:     "Current password is incorrect.",
                    statusCode: StatusCodes.Status401Unauthorized);

            // Step 2: Get email for confirmation
            email = await authConn.ExecuteScalarAsync<string>(
                $"""
                SELECT email FROM {profile}
                WHERE tenant_id = @TenantId AND user_id = @UserId
                """,
                new { request.TenantId, request.UserId },
                commandTimeout: 10);
        }

        // Step 3: Hash new password and generate confirmation token
        string newPasswordHash    = Argon2idHasher.Hash(request.NewPassword!);
        string confirmationToken  = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        string tokenHash          = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(confirmationToken)));
        var    now                = PresetsDbHelper.UtcNow();
        int    expiryMins         = changePasswordMonitor.CurrentValue.TokenExpiryMinutes;

        // Step 4: Delete existing pending requests, insert new one
        using (IDbConnection presetsConn = connectionFactory.CreateFromConnectionString(
                   presetsDb.ConnectionString, presetsDb.DbType))
        {
            await presetsConn.ExecuteAsync(
                $"""
                DELETE FROM {changeReq}
                WHERE tenant_id    = @TenantId
                AND   user_id      = @UserId
                AND   confirmed_on IS NULL
                """,
                new { request.TenantId, request.UserId },
                commandTimeout: 10);

            await presetsConn.ExecuteAsync(
                $"""
                INSERT INTO {changeReq}
                    (id, tenant_id, user_id, new_password_hash, token_hash, expires_on, created_on)
                VALUES
                    (@Id, @TenantId, @UserId, @NewPasswordHash, @TokenHash, @ExpiresOn, @Now)
                """,
                new
                {
                    Id              = Guid.NewGuid(),
                    request.TenantId,
                    request.UserId,
                    NewPasswordHash = newPasswordHash,
                    TokenHash       = tokenHash,
                    ExpiresOn       = now.AddMinutes(expiryMins),
                    Now             = now,
                },
                commandTimeout: 10);
        }

        // Step 5: Send confirmation email
        if (!string.IsNullOrWhiteSpace(email))
        {
            string appBaseUrl = configuration["AppBaseUrl"] ?? "http://localhost:3000";
            string link       = $"{appBaseUrl.TrimEnd('/')}/confirm-password-change?token={Uri.EscapeDataString(confirmationToken)}";

            await emailSender.SendAsync(
                email,
                "Confirm your Nova password change",
                $"""
                You recently requested a password change on your Nova account.

                To confirm and apply your new password, click the link below:
                {link}

                This link expires in {expiryMins} minutes.

                If you did not request this change, please ignore this email — your password will remain unchanged.
                """,
                ct);
        }

        return TypedResults.Ok(new
        {
            message = "A confirmation email has been sent to your registered email address. Your new password will take effect once you confirm via the email link."
        });
    }

    private sealed record AuthRow(string UserId, string? PasswordHash);

    private sealed record ChangePasswordRequest : RequestContext
    {
        public string? CurrentPassword { get; init; }
        public string? NewPassword     { get; init; }
    }
}
