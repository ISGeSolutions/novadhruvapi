using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.CommonUX.Api.Services;
using Nova.Shared.Data;

namespace Nova.CommonUX.Api.Endpoints.Auth;

/// <summary>
/// Social link — complete: <c>POST /api/v1/auth/social/link/complete</c>
/// Exchanges the OAuth callback token and writes or updates the social identity row
/// for the authenticated user.
/// </summary>
public static class SocialLinkCompleteEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/auth/social/link/complete", HandleAsync)
             .RequireAuthorization()
             .WithName("AuthSocialLinkComplete");
    }

    private static async Task<IResult> HandleAsync(
        SocialLinkCompleteRequest    request,
        HttpContext                  httpContext,
        IDbConnectionFactory         connectionFactory,
        IOptions<AuthDbSettings>     authDbOptions,
        ISocialTokenVerifier         verifier,
        CancellationToken            ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Provider))    errors["provider"]     = ["provider is required."];
        if (string.IsNullOrWhiteSpace(request.SocialToken)) errors["social_token"] = ["social_token is required."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        ClaimsPrincipal user     = httpContext.User;
        string?         tenantId = user.FindFirstValue("tenant_id");
        string?         userId   = user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId))
            return TypedResults.Problem(title: "Unauthorized", detail: "Invalid token claims.", statusCode: 401);

        SocialIdentity? identity = await verifier.VerifyAsync(request.Provider!, request.SocialToken!, ct);
        if (identity is null)
            return TypedResults.Problem(
                title:      "Unauthorized",
                detail:     "Social token verification failed.",
                statusCode: StatusCodes.Status401Unauthorized);

        AuthDbSettings authDb  = authDbOptions.Value;
        ISqlDialect    dialect = AuthDbHelper.Dialect(authDb.DbType);
        string         social  = dialect.TableRef("nova_auth", "tenant_user_social_identity");
        var            now     = AuthDbHelper.UtcNow();

        using IDbConnection connection = connectionFactory.CreateFromConnectionString(
            authDb.ConnectionString, authDb.DbType);

        // Check for existing resolved link (409 Conflict)
        bool alreadyLinked = await connection.ExecuteScalarAsync<int>(
            $"""
            SELECT COUNT(1) FROM {social}
            WHERE tenant_id = @TenantId AND provider = @Provider AND provider_user_id = @ProviderId
            """,
            new { TenantId = tenantId, Provider = request.Provider, ProviderId = identity.ProviderId },
            commandTimeout: 10) > 0;

        if (alreadyLinked)
            return TypedResults.Problem(
                title:      "Conflict",
                detail:     "This provider is already linked to an account.",
                statusCode: StatusCodes.Status409Conflict);

        // Check for admin-provisioned pending row (email match, provider_user_id IS NULL)
        PendingRow? pending = await connection.QuerySingleOrDefaultAsync<PendingRow>(
            $"""
            SELECT user_id FROM {social}
            WHERE tenant_id = @TenantId AND provider = @Provider AND provider_email = @Email
              AND provider_user_id IS NULL AND frz_ind = {dialect.BooleanLiteral(false)}
            """,
            new { TenantId = tenantId, Provider = request.Provider, Email = identity.Email },
            commandTimeout: 10);

        if (pending is not null && pending.UserId == userId)
        {
            // Resolve pending link
            await connection.ExecuteAsync(
                $"""
                UPDATE {social}
                SET provider_user_id = @ProviderId, linked_on = @Now,
                    updated_on = @Now, updated_by = @UserId, updated_at = 'Nova.CommonUX.Api'
                WHERE tenant_id = @TenantId AND provider = @Provider AND user_id = @UserId AND provider_user_id IS NULL
                """,
                new { ProviderId = identity.ProviderId, Now = now, TenantId = tenantId,
                      Provider = request.Provider, UserId = userId },
                commandTimeout: 10);
        }
        else
        {
            // Insert new link
            await connection.ExecuteAsync(
                $"""
                INSERT INTO {social}
                    (tenant_id, user_id, provider, provider_user_id, provider_email, linked_on,
                     frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
                VALUES
                    (@TenantId, @UserId, @Provider, @ProviderId, @Email, @Now,
                     {dialect.BooleanLiteral(false)}, @UserId, @Now, @UserId, @Now, 'Nova.CommonUX.Api')
                """,
                new { TenantId = tenantId, UserId = userId, Provider = request.Provider,
                      ProviderId = identity.ProviderId, Email = identity.Email, Now = now },
                commandTimeout: 10);
        }

        return TypedResults.Ok(new
        {
            message        = "Social account linked successfully.",
            provider       = request.Provider,
            provider_email = identity.Email
        });
    }

    private sealed record PendingRow(string UserId);

    private sealed record SocialLinkCompleteRequest
    {
        public string? Provider    { get; init; }
        public string? SocialToken { get; init; }
    }
}
