namespace Nova.CommonUX.Api.Models;

/// <summary>Response returned on successful login, 2FA verify, magic-link verify, and social complete.</summary>
public sealed record LoginResponse(
    string       Token,
    int          ExpiresIn,
    bool         Requires2Fa,
    string?      SessionToken,
    string?      RefreshToken,
    UserInfo?    User);

/// <summary>Minimal user identity embedded in login responses.</summary>
public sealed record UserInfo(
    string  UserId,
    string  Name,
    string  Email,
    string? AvatarUrl);

/// <summary>Response returned by <c>POST /api/v1/auth/token</c> (M2M).</summary>
public sealed record AppTokenResponse(string Token, int ExpiresIn);

/// <summary>Response returned by <c>POST /api/v1/auth/refresh</c>.</summary>
public sealed record RefreshResponse(string Token, int ExpiresIn, string RefreshToken);

/// <summary>Generic message response.</summary>
public sealed record MessageResponse(string Message);
