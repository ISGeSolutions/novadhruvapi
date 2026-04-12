using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.CommonUX.Api.Models;
using Nova.CommonUX.Api.Services;

namespace Nova.CommonUX.Api.Endpoints.Auth;

/// <summary>
/// Token refresh: <c>POST /api/v1/auth/refresh</c>
/// Requires a valid Bearer JWT. Exchanges an opaque refresh token for a new JWT
/// and a rotated refresh token (sliding window).
///
/// Refresh tokens are stored in session store under key
/// <c>refresh:{tenantId}:{userId}:{token}</c> → <c>"valid"</c>.
/// This allows O(1) lookup on refresh and prefix-pattern deletion on password reset.
/// </summary>
public static class RefreshEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/auth/refresh", HandleAsync)
             .RequireAuthorization()
             .WithName("AuthRefresh");
    }

    private static async Task<IResult> HandleAsync(
        RefreshRequest                request,
        HttpContext                   httpContext,
        IOptionsMonitor<AuthSettings> authMonitor,
        ITokenService                 tokenService,
        ISessionStore                 sessionStore,
        CancellationToken             ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["refresh_token"] = ["refresh_token is required."] },
                title: "Validation failed");

        // JWT is already validated by RequireAuthorization — extract claims
        ClaimsPrincipal user     = httpContext.User;
        string?         tenantId = user.FindFirstValue("tenant_id");
        string?         userId   = user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId))
            return Unauthorized();

        // Lookup stored refresh token: refresh:{tenantId}:{userId}:{token}
        string  lookupKey = $"refresh:{tenantId}:{userId}:{request.RefreshToken}";
        string? stored    = await sessionStore.GetAsync(lookupKey, ct);

        if (stored is null)
            return Unauthorized();

        // Issue new JWT and rotate refresh token
        var (newToken, expiresIn) = tokenService.IssueJwt(tenantId, userId, []);
        string newRefreshToken    = tokenService.GenerateRefreshToken();
        string newRefreshKey      = $"refresh:{tenantId}:{userId}:{newRefreshToken}";

        await sessionStore.DeleteAsync(lookupKey, ct);
        await sessionStore.SetAsync(
            newRefreshKey,
            "valid",
            TimeSpan.FromDays(authMonitor.CurrentValue.RefreshTokenLifetimeDays),
            ct);

        return TypedResults.Ok(new RefreshResponse(newToken, expiresIn, newRefreshToken));
    }

    private static IResult Unauthorized() =>
        TypedResults.Problem(
            title:      "Unauthorized",
            detail:     "JWT expired or refresh token not found.",
            statusCode: StatusCodes.Status401Unauthorized);

    private sealed record RefreshRequest
    {
        public string? RefreshToken { get; init; }
    }
}
