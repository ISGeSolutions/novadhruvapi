namespace Nova.CommonUX.Api.Configuration;

/// <summary>
/// Authentication operational settings. Stored in <c>opsettings.json → Auth</c>.
/// All values are hot-reloadable via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>.
/// </summary>
public sealed class AuthSettings
{
    public const string SectionName = "Auth";

    /// <summary>Failed login attempts before lockout is triggered. Default: 5.</summary>
    public int FailedLoginMaxAttempts { get; set; } = 5;

    /// <summary>Duration of login lockout in minutes. Default: 15.</summary>
    public int FailedLoginLockoutMinutes { get; set; } = 15;

    /// <summary>TTL in minutes for 2FA session tokens stored in Redis/InMemory. Default: 5.</summary>
    public int TwoFaSessionExpiryMinutes { get; set; } = 5;

    /// <summary>Sliding window TTL in days for opaque refresh tokens. Default: 7.</summary>
    public int RefreshTokenLifetimeDays { get; set; } = 7;

    /// <summary>Expiry in minutes for password reset tokens. Default: 60.</summary>
    public int PasswordResetTokenExpiryMinutes { get; set; } = 60;

    /// <summary>Expiry in minutes for magic link tokens. Default: 15.</summary>
    public int MagicLinkTokenExpiryMinutes { get; set; } = 15;
}
