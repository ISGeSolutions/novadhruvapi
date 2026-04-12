using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.CommonUX.Api.Models;
using Nova.CommonUX.Api.Services;
using Nova.Shared.Data;

namespace Nova.CommonUX.Api.Endpoints.Auth;

/// <summary>
/// Social login — complete: <c>POST /api/v1/auth/social</c>
/// Verifies the OAuth social token with the provider, looks up the linked account,
/// and issues a JWT. Pre-provisioning model — no auto-creation.
/// </summary>
public static class SocialCompleteEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/auth/social/complete", HandleAsync)
             .AllowAnonymous()
             .WithName("AuthSocialComplete");
    }

    private static async Task<IResult> HandleAsync(
        SocialCompleteRequest         request,
        IDbConnectionFactory          connectionFactory,
        IOptions<AuthDbSettings>      authDbOptions,
        IOptionsMonitor<AuthSettings> authMonitor,
        ITokenService                 tokenService,
        ISessionStore                 sessionStore,
        ISocialTokenVerifier          verifier,
        CancellationToken             ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.TenantId))    errors["tenant_id"]    = ["tenant_id is required."];
        if (string.IsNullOrWhiteSpace(request.Provider))    errors["provider"]     = ["provider is required."];
        if (string.IsNullOrWhiteSpace(request.SocialToken)) errors["social_token"] = ["social_token is required."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        // Verify social token with the provider
        SocialIdentity? identity = await verifier.VerifyAsync(request.Provider!, request.SocialToken!, ct);
        if (identity is null)
            return TypedResults.Problem(
                title:      "Unauthorized",
                detail:     "Social token verification failed.",
                statusCode: StatusCodes.Status401Unauthorized);

        AuthDbSettings authDb   = authDbOptions.Value;
        ISqlDialect    dialect  = AuthDbHelper.Dialect(authDb.DbType);
        string         social   = dialect.TableRef("nova_auth", "tenant_user_social_identity");
        string         profile  = dialect.TableRef("nova_auth", "tenant_user_profile");
        var            now      = AuthDbHelper.UtcNow();

        using IDbConnection connection = connectionFactory.CreateFromConnectionString(
            authDb.ConnectionString, authDb.DbType);

        // Step 1: look up by (tenant_id, provider, provider_user_id)
        SocialRow? row = await connection.QuerySingleOrDefaultAsync<SocialRow>(
            $"""
            SELECT user_id, provider_user_id, provider_email
            FROM {social}
            WHERE tenant_id = @TenantId AND provider = @Provider AND provider_user_id = @ProviderId
              AND frz_ind = {dialect.BooleanLiteral(false)}
            """,
            new { request.TenantId, Provider = request.Provider, ProviderId = identity.ProviderId },
            commandTimeout: 10);

        // Step 2: if not found, look up by email where provider_user_id IS NULL (pending admin-provisioned link)
        if (row is null)
        {
            row = await connection.QuerySingleOrDefaultAsync<SocialRow>(
                $"""
                SELECT user_id, provider_user_id, provider_email
                FROM {social}
                WHERE tenant_id = @TenantId AND provider = @Provider AND provider_email = @Email
                  AND provider_user_id IS NULL AND frz_ind = {dialect.BooleanLiteral(false)}
                """,
                new { request.TenantId, Provider = request.Provider, Email = identity.Email },
                commandTimeout: 10);

            if (row is not null)
            {
                // Resolve the pending link
                await connection.ExecuteAsync(
                    $"""
                    UPDATE {social}
                    SET provider_user_id = @ProviderId, linked_on = @Now,
                        updated_on = @Now, updated_by = @UserId, updated_at = 'Nova.CommonUX.Api'
                    WHERE tenant_id = @TenantId AND provider = @Provider AND user_id = @UserId AND provider_user_id IS NULL
                    """,
                    new { ProviderId = identity.ProviderId, Now = now, TenantId = request.TenantId,
                          Provider = request.Provider, UserId = row.UserId },
                    commandTimeout: 10);
            }
        }

        if (row is null)
            return TypedResults.Problem(
                title:      "Unauthorized",
                detail:     "No linked account found for this provider identity.",
                statusCode: StatusCodes.Status401Unauthorized);

        // Load profile and issue JWT
        ProfileRow? prof = await connection.QuerySingleOrDefaultAsync<ProfileRow>(
            $"""
            SELECT display_name, email, avatar_url
            FROM {profile}
            WHERE tenant_id = @TenantId AND user_id = @UserId AND frz_ind = {dialect.BooleanLiteral(false)}
            """,
            new { request.TenantId, UserId = row.UserId },
            commandTimeout: 10);

        var (token, expiresIn) = tokenService.IssueJwt(request.TenantId!, row.UserId, []);
        string refreshToken    = tokenService.GenerateRefreshToken();
        string refreshKey      = $"refresh:{request.TenantId}:{row.UserId}:{refreshToken}";

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
            User: prof is null ? null : new UserInfo(row.UserId, prof.DisplayName, prof.Email, prof.AvatarUrl)));
    }

    private sealed record SocialRow(string UserId, string? ProviderUserId, string ProviderEmail);
    private sealed record ProfileRow(string DisplayName, string Email, string? AvatarUrl);

    private sealed record SocialCompleteRequest
    {
        public string? TenantId    { get; init; }
        public string? Provider    { get; init; }
        public string? SocialToken { get; init; }
    }
}
