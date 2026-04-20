using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.CommonUX.Api.Models;
using Nova.CommonUX.Api.Services;
using Nova.Shared.Data;
using Nova.Shared.Security;
using OtpNet;

namespace Nova.CommonUX.Api.Endpoints.Auth;

/// <summary>
/// 2FA verification: <c>POST /api/v1/auth/verify-2fa</c>
/// Verifies the TOTP code against a prior session_token and issues a JWT on success.
/// TOTP tolerance: ±1 step (±30 seconds).
/// </summary>
public static class Verify2FaEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/auth/verify-2fa", HandleAsync)
             .AllowAnonymous()
             .WithName("AuthVerify2Fa");
    }

    private static async Task<IResult> HandleAsync(
        Verify2FaRequest              request,
        IDbConnectionFactory          connectionFactory,
        IOptions<AuthDbSettings>      authDbOptions,
        IOptionsMonitor<AuthSettings> authMonitor,
        ITokenService                 tokenService,
        ISessionStore                 sessionStore,
        ICipherService                cipher,
        CancellationToken             ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.SessionToken)) errors["session_token"] = ["session_token is required."];
        if (string.IsNullOrWhiteSpace(request.Code))        errors["code"]           = ["code is required."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        // Resolve session_token → (tenant_id, user_id)
        string? payload = await sessionStore.GetAsync($"2fa:{request.SessionToken}", ct);
        if (payload is null)
            return Unauthorized();

        string? tenantId, userId;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(payload);
            tenantId = doc.RootElement.GetProperty("TenantId").GetString();
            userId   = doc.RootElement.GetProperty("UserId").GetString();
        }
        catch
        {
            return Unauthorized();
        }

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId))
            return Unauthorized();

        AuthDbSettings authDb  = authDbOptions.Value;
        ISqlDialect    dialect = AuthDbHelper.Dialect(authDb.DbType);
        string         auth    = dialect.TableRef("nova_auth", "tenant_user_auth");
        string         profile = dialect.TableRef("nova_auth", "tenant_user_profile");

        using IDbConnection connection = connectionFactory.CreateFromConnectionString(
            authDb.ConnectionString, authDb.DbType);

        string? totpEncrypted = await connection.ExecuteScalarAsync<string?>(
            $"SELECT totp_secret_encrypted FROM {auth} WHERE tenant_id = @TenantId AND user_id = @UserId",
            new { TenantId = tenantId, UserId = userId },
            commandTimeout: 10);

        if (string.IsNullOrEmpty(totpEncrypted))
            return Unauthorized();

        string  totpSecretPlain = cipher.Decrypt(totpEncrypted);
        byte[]  secretBytes     = Base32Encoding.ToBytes(totpSecretPlain);
        var     totp            = new Totp(secretBytes);

        bool valid = totp.VerifyTotp(
            request.Code!,
            out _,
            new VerificationWindow(previous: 1, future: 1));

        if (!valid)
            return Unauthorized();

        // Invalidate the 2FA session token — single-use
        await sessionStore.DeleteAsync($"2fa:{request.SessionToken}", ct);

        // Load profile and issue JWT
        ProfileRow? prof = await connection.QuerySingleOrDefaultAsync<ProfileRow>(
            $"""
            SELECT display_name, email, avatar_url, program_id_root
            FROM {profile}
            WHERE tenant_id = @TenantId AND user_id = @UserId AND frz_ind = {dialect.BooleanLiteral(false)}
            """,
            new { TenantId = tenantId, UserId = userId },
            commandTimeout: 10);

        var (token, expiresIn) = tokenService.IssueJwt(tenantId, userId, []);
        string refreshToken    = tokenService.GenerateRefreshToken();
        string refreshKey      = $"refresh:{tenantId}:{userId}:{refreshToken}";

        await sessionStore.SetAsync(
            refreshKey,
            "valid",
            TimeSpan.FromDays(authMonitor.CurrentValue.RefreshTokenLifetimeDays),
            ct);

        return TypedResults.Ok(new LoginResponse(
            Token:        token,
            ExpiresIn:    expiresIn,
            Requires2Fa:  false,
            SessionToken: null,
            RefreshToken: refreshToken,
            User: prof is null ? null : new UserInfo(userId, prof.DisplayName, prof.Email, prof.AvatarUrl, prof.ProgramIdRoot)));
    }

    private static IResult Unauthorized() =>
        TypedResults.Problem(
            title:      "Unauthorized",
            detail:     "Invalid or expired session token or incorrect code.",
            statusCode: StatusCodes.Status401Unauthorized);

    private sealed record ProfileRow(string DisplayName, string Email, string? AvatarUrl, string? ProgramIdRoot);

    private sealed record Verify2FaRequest
    {
        public string? SessionToken { get; init; }
        public string? Code         { get; init; }
    }
}
