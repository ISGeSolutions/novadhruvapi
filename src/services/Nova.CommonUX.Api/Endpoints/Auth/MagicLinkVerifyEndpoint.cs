using System.Data;
using System.Text;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.CommonUX.Api.Models;
using Nova.CommonUX.Api.Services;
using Nova.Shared.Data;

namespace Nova.CommonUX.Api.Endpoints.Auth;

/// <summary>
/// Magic link verify: <c>POST /api/v1/auth/magic-link/verify</c>
/// Exchanges a magic link token for a JWT. Bypasses 2FA.
/// </summary>
public static class MagicLinkVerifyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/auth/magic-link/verify", HandleAsync)
             .AllowAnonymous()
             .WithName("AuthMagicLinkVerify");
    }

    private static async Task<IResult> HandleAsync(
        MagicLinkVerifyRequest        request,
        IDbConnectionFactory          connectionFactory,
        IOptions<AuthDbSettings>      authDbOptions,
        IOptionsMonitor<AuthSettings> authMonitor,
        ITokenService                 tokenService,
        ISessionStore                 sessionStore,
        CancellationToken             ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["token"] = ["token is required."] },
                title: "Validation failed");

        AuthDbSettings authDb     = authDbOptions.Value;
        ISqlDialect    dialect    = AuthDbHelper.Dialect(authDb.DbType);
        string         authTokens = dialect.TableRef("nova_auth", "tenant_auth_tokens");
        string         profile    = dialect.TableRef("nova_auth", "tenant_user_profile");

        string tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token!)));
        var    now       = AuthDbHelper.UtcNow();

        using IDbConnection connection = connectionFactory.CreateFromConnectionString(
            authDb.ConnectionString, authDb.DbType);

        TokenRow? row = await connection.QuerySingleOrDefaultAsync<TokenRow>(
            $"""
            SELECT id, tenant_id, user_id, expires_on, used_on
            FROM {authTokens}
            WHERE token_hash = @TokenHash AND token_type = 'magic_link'
            """,
            new { TokenHash = tokenHash },
            commandTimeout: 10);

        if (row is null || row.UsedOn.HasValue || row.ExpiresOn <= now)
            return TypedResults.Problem(
                title:      "Unauthorized",
                detail:     "Invalid or expired magic link token.",
                statusCode: StatusCodes.Status401Unauthorized);

        // Atomically claim the token — guards against two simultaneous requests with the same token.
        int tokenUsed = await connection.ExecuteAsync(
            $"UPDATE {authTokens} SET used_on = @Now WHERE id = @Id AND used_on IS NULL",
            new { Now = now, row.Id },
            commandTimeout: 10);

        if (tokenUsed == 0)
            return TypedResults.Problem(
                title:      "Unauthorized",
                detail:     "Invalid or expired magic link token.",
                statusCode: StatusCodes.Status401Unauthorized);

        ProfileRow? prof = await connection.QuerySingleOrDefaultAsync<ProfileRow>(
            $"""
            SELECT display_name, email, avatar_url, program_id_root
            FROM {profile}
            WHERE tenant_id = @TenantId AND user_id = @UserId AND frz_ind = {dialect.BooleanLiteral(false)}
            """,
            new { TenantId = row.TenantId, UserId = row.UserId },
            commandTimeout: 10);

        var (token, expiresIn) = tokenService.IssueJwt(row.TenantId, row.UserId, []);
        string refreshToken    = tokenService.GenerateRefreshToken();
        string refreshKey      = $"refresh:{row.TenantId}:{row.UserId}:{refreshToken}";

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
            User: prof is null ? null : new UserInfo(row.UserId, prof.DisplayName, prof.Email, prof.AvatarUrl, prof.ProgramIdRoot)));
    }

    private sealed record TokenRow
    {
        public Guid            Id        { get; set; }
        public string          TenantId  { get; set; } = string.Empty;
        public string          UserId    { get; set; } = string.Empty;
        public DateTimeOffset  ExpiresOn { get; set; }
        public DateTimeOffset? UsedOn    { get; set; }
    }
    private sealed record ProfileRow(string DisplayName, string Email, string? AvatarUrl, string? ProgramIdRoot);

    private sealed record MagicLinkVerifyRequest
    {
        public string? Token { get; set; }
    }
}
