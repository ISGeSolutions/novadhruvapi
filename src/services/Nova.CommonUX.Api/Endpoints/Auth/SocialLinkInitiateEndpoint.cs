using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;

namespace Nova.CommonUX.Api.Endpoints.Auth;

/// <summary>
/// Social link — initiate: <c>POST /api/v1/auth/social/link</c>
/// Authenticated user links a new social provider. Returns OAuth redirect URL.
/// The state parameter encodes (tenant_id, user_id) so the complete step can resolve the account.
/// </summary>
public static class SocialLinkInitiateEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/auth/social/link", HandleLinkInitiateAsync)
             .RequireAuthorization()
             .WithName("AuthSocialLinkInitiate");
    }

    private static IResult HandleLinkInitiateAsync(
        SocialLinkInitiateRequest          request,
        HttpContext                         httpContext,
        IOptionsMonitor<SocialLoginSettings> socialMonitor)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Provider))    errors["provider"]     = ["provider is required."];
        if (string.IsNullOrWhiteSpace(request.CallbackUrl)) errors["callback_url"] = ["callback_url is required."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        ClaimsPrincipal user     = httpContext.User;
        string?         tenantId = user.FindFirstValue("tenant_id");
        string?         userId   = user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId))
            return TypedResults.Problem(title: "Unauthorized", detail: "Invalid token claims.", statusCode: 401);

        // state encodes callback_url + authenticated identity
        string statePayload = $"{Uri.EscapeDataString(request.CallbackUrl!)}|{tenantId}|{userId}";
        string state        = Uri.EscapeDataString(statePayload);

        SocialLoginSettings settings = socialMonitor.CurrentValue;
        string? redirectUrl = request.Provider!.ToLowerInvariant() switch
        {
            "google" => $"https://accounts.google.com/o/oauth2/v2/auth" +
                        $"?client_id={Uri.EscapeDataString(settings.Google.ClientId)}" +
                        $"&redirect_uri={Uri.EscapeDataString(request.CallbackUrl!)}" +
                        $"&response_type=code&scope=openid%20email%20profile&state={state}",

            "microsoft" => $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize" +
                           $"?client_id={Uri.EscapeDataString(settings.Microsoft.ClientId)}" +
                           $"&redirect_uri={Uri.EscapeDataString(request.CallbackUrl!)}" +
                           $"&response_type=code&scope=openid%20email%20profile&state={state}",

            "apple" => $"https://appleid.apple.com/auth/authorize" +
                       $"?client_id={Uri.EscapeDataString(settings.Apple.ClientId)}" +
                       $"&redirect_uri={Uri.EscapeDataString(request.CallbackUrl!)}" +
                       $"&response_type=code%20id_token&scope=name%20email&state={state}",

            _ => null
        };

        if (redirectUrl is null)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["provider"] = [$"Unknown provider '{request.Provider}'."] },
                title: "Validation failed");

        return TypedResults.Ok(new { redirect_url = redirectUrl });
    }

    private sealed record SocialLinkInitiateRequest
    {
        public string? Provider    { get; init; }
        public string? CallbackUrl { get; init; }
    }
}
