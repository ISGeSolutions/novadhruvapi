using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;

namespace Nova.CommonUX.Api.Endpoints.Auth;

/// <summary>
/// Social login — initiate: <c>POST /api/v1/auth/social</c>
/// Builds and returns the OAuth redirect URL for the requested provider.
/// No DB access — config lookup only.
/// </summary>
public static class SocialInitiateEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/auth/social", HandleAsync)
             .AllowAnonymous()
             .WithName("AuthSocialInitiate");
    }

    private static IResult HandleAsync(
        SocialInitiateRequest              request,
        IOptionsMonitor<SocialLoginSettings> socialMonitor)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Provider))    errors["provider"]     = ["provider is required. Valid values: google, microsoft, apple."];
        if (string.IsNullOrWhiteSpace(request.CallbackUrl)) errors["callback_url"] = ["callback_url is required."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        string? redirectUrl = BuildRedirectUrl(request.Provider!, request.CallbackUrl!, socialMonitor.CurrentValue);

        if (redirectUrl is null)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["provider"] = [$"Unknown provider '{request.Provider}'. Valid values: google, microsoft, apple."] },
                title: "Validation failed");

        return TypedResults.Ok(new { redirect_url = redirectUrl });
    }

    private static string? BuildRedirectUrl(
        string              provider,
        string              callbackUrl,
        SocialLoginSettings settings)
    {
        // state encodes the callback_url so the complete step can redirect correctly
        string state = Uri.EscapeDataString(callbackUrl);

        return provider.ToLowerInvariant() switch
        {
            "google" => $"https://accounts.google.com/o/oauth2/v2/auth" +
                        $"?client_id={Uri.EscapeDataString(settings.Google.ClientId)}" +
                        $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
                        $"&response_type=code" +
                        $"&scope=openid%20email%20profile" +
                        $"&state={state}",

            "microsoft" => $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize" +
                           $"?client_id={Uri.EscapeDataString(settings.Microsoft.ClientId)}" +
                           $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
                           $"&response_type=code" +
                           $"&scope=openid%20email%20profile" +
                           $"&state={state}",

            "apple" => $"https://appleid.apple.com/auth/authorize" +
                       $"?client_id={Uri.EscapeDataString(settings.Apple.ClientId)}" +
                       $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
                       $"&response_type=code%20id_token" +
                       $"&scope=name%20email" +
                       $"&state={state}",

            _ => null
        };
    }

    private sealed record SocialInitiateRequest
    {
        public string? Provider    { get; init; }
        public string? CallbackUrl { get; init; }
    }
}
