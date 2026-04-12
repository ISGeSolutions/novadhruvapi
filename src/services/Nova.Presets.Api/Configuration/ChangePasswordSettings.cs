namespace Nova.Presets.Api.Configuration;

/// <summary>
/// Change-password flow settings. Stored in <c>opsettings.json → ChangePassword</c>.
/// Hot-reloadable via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>.
/// </summary>
public sealed class ChangePasswordSettings
{
    public const string SectionName = "ChangePassword";

    /// <summary>TTL in minutes for the email confirmation token. Default: 60.</summary>
    public int TokenExpiryMinutes { get; set; } = 60;
}
