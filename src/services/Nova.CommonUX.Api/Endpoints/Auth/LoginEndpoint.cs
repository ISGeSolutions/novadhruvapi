using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.CommonUX.Api.Models;
using Nova.CommonUX.Api.Services;
using Nova.Shared.Data;

namespace Nova.CommonUX.Api.Endpoints.Auth;

/// <summary>
/// User login: <c>POST /api/v1/auth/login</c>
///
/// Returns JWT directly (no 2FA) or a session_token if TOTP is enabled.
/// Enforces failed-login lockout via <c>FailedLoginMaxAttempts</c> and
/// <c>FailedLoginLockoutMinutes</c> from opsettings.
/// </summary>
public static class LoginEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/auth/login", HandleAsync)
             .AllowAnonymous()
             .WithName("AuthLogin");
    }

    private static async Task<IResult> HandleAsync(
        LoginRequest                    request,
        IDbConnectionFactory            connectionFactory,
        IOptions<AuthDbSettings>        authDbOptions,
        IOptionsMonitor<AuthSettings>   authMonitor,
        ITokenService                   tokenService,
        ISessionStore                   sessionStore,
        CancellationToken               ct)
    {
        // Validate input
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.TenantId))   errors["tenant_id"] = ["tenant_id is required."];
        if (string.IsNullOrWhiteSpace(request.UserId))     errors["user_id"]   = ["user_id must be at least 1 character."];
        if ((request.Password?.Length ?? 0) < 8)           errors["password"]  = ["password must be at least 8 characters."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        AuthDbSettings authDb   = authDbOptions.Value;
        AuthSettings   authOpts = authMonitor.CurrentValue;
        ISqlDialect    dialect  = AuthDbHelper.Dialect(authDb.DbType);

        string userAuth = dialect.TableRef("nova_auth", "tenant_user_auth");

        using IDbConnection connection = connectionFactory.CreateFromConnectionString(
            authDb.ConnectionString, authDb.DbType);

        AuthRow? row = await connection.QuerySingleOrDefaultAsync<AuthRow>(
            $"""
            SELECT tenant_id, user_id, password_hash, totp_enabled, totp_secret_encrypted,
                   failed_login_count, locked_until, last_login_on, frz_ind
            FROM {userAuth}
            WHERE tenant_id = @TenantId AND user_id = @UserId
            """,
            new { request.TenantId, request.UserId },
            commandTimeout: 10);

        // Return 401 for any auth failure — do not distinguish between "not found" and "wrong password"
        if (row is null || row.FrzInd)
            return Unauthorized();

        if (row.LockedUntil.HasValue && row.LockedUntil.Value > DateTimeOffset.UtcNow)
            return Unauthorized();

        bool passwordOk = row.PasswordHash is not null &&
                          Argon2idHasher.Verify(request.Password!, row.PasswordHash);

        if (!passwordOk)
        {
            int newCount = row.FailedLoginCount + 1;
            DateTimeOffset? lockUntil = newCount >= authOpts.FailedLoginMaxAttempts
                ? DateTimeOffset.UtcNow.AddMinutes(authOpts.FailedLoginLockoutMinutes)
                : null;

            await connection.ExecuteAsync(
                $"""
                UPDATE {userAuth}
                SET failed_login_count = @Count,
                    locked_until       = @LockUntil,
                    updated_on         = @Now,
                    updated_by         = 'system',
                    updated_at         = 'Nova.CommonUX.Api'
                WHERE tenant_id = @TenantId AND user_id = @UserId
                """,
                new { Count = newCount, LockUntil = lockUntil, Now = AuthDbHelper.UtcNow(),
                      request.TenantId, request.UserId },
                commandTimeout: 10);

            return Unauthorized();
        }

        // Success — reset failed count and update last_login_on
        await connection.ExecuteAsync(
            $"""
            UPDATE {userAuth}
            SET failed_login_count = 0,
                locked_until       = NULL,
                last_login_on      = @Now,
                updated_on         = @Now,
                updated_by         = @UserId,
                updated_at         = 'Nova.CommonUX.Api'
            WHERE tenant_id = @TenantId AND user_id = @UserId
            """,
            new { Now = AuthDbHelper.UtcNow(), request.TenantId, request.UserId },
            commandTimeout: 10);

        // 2FA required — issue session_token, do NOT issue JWT yet
        if (row.TotpEnabled)
        {
            string sessionToken = tokenService.GenerateRefreshToken(); // reuse entropy source
            string payload      = JsonSerializer.Serialize(new { request.TenantId, request.UserId });

            await sessionStore.SetAsync(
                $"2fa:{sessionToken}",
                payload,
                TimeSpan.FromMinutes(authOpts.TwoFaSessionExpiryMinutes),
                ct);

            return TypedResults.Ok(new LoginResponse(
                Token:        string.Empty,
                ExpiresIn:    0,
                Requires2Fa:  true,
                SessionToken: sessionToken,
                RefreshToken: null,
                User:         null));
        }

        // No 2FA — load profile and issue JWT
        string userProfile = dialect.TableRef("nova_auth", "tenant_user_profile");
        ProfileRow? profile = await connection.QuerySingleOrDefaultAsync<ProfileRow>(
            $"""
            SELECT display_name, email, avatar_url, program_id_root
            FROM {userProfile}
            WHERE tenant_id = @TenantId AND user_id = @UserId AND frz_ind = {dialect.BooleanLiteral(false)}
            """,
            new { request.TenantId, request.UserId },
            commandTimeout: 10);

        var (token, expiresIn) = tokenService.IssueJwt(request.TenantId, request.UserId, []);
        string refreshToken    = tokenService.GenerateRefreshToken();
        // Store as: refresh:{tenantId}:{userId}:{token} → "valid"
        // This allows O(1) lookup on refresh and O(n) prefix scan on password reset.
        string refreshKey = $"refresh:{request.TenantId}:{request.UserId}:{refreshToken}";

        await sessionStore.SetAsync(
            refreshKey,
            "valid",
            TimeSpan.FromDays(authOpts.RefreshTokenLifetimeDays),
            ct);

        return TypedResults.Ok(new LoginResponse(
            Token:        token,
            ExpiresIn:    expiresIn,
            Requires2Fa:  false,
            SessionToken: null,
            RefreshToken: refreshToken,
            User: profile is null ? null : new UserInfo(
                UserId:        request.UserId,
                Name:          profile.DisplayName,
                Email:         profile.Email,
                AvatarUrl:     profile.AvatarUrl,
                ProgramIdRoot: profile.ProgramIdRoot)));
    }

    private static IResult Unauthorized() =>
        TypedResults.Problem(
            title:      "Unauthorized",
            detail:     "Invalid credentials.",
            statusCode: StatusCodes.Status401Unauthorized);

    // Init-property record: gives Dapper a parameterless constructor so it uses property-setter
    // materialisation, which correctly handles Nullable<DateTimeOffset> columns.
    // Positional records fail because GetFieldType() returns non-nullable DateTimeOffset and
    // the constructor signature Nullable<DateTimeOffset> != DateTimeOffset at runtime.
    private sealed record AuthRow
    {
        public string          TenantId            { get; set; } = string.Empty;
        public string          UserId              { get; set; } = string.Empty;
        public string?         PasswordHash        { get; set; }
        public bool            TotpEnabled         { get; set; }
        public string?         TotpSecretEncrypted { get; set; }
        public int             FailedLoginCount    { get; set; }
        public DateTimeOffset? LockedUntil         { get; set; }
        public DateTimeOffset? LastLoginOn         { get; set; }
        public bool            FrzInd              { get; set; }
    }

    private sealed record ProfileRow(string DisplayName, string Email, string? AvatarUrl, string? ProgramIdRoot);

    private sealed record LoginRequest
    {
        public string  TenantId { get; set; } = string.Empty;
        public string  UserId   { get; set; } = string.Empty;
        public string? Password  { get; set; }
    }
}
