namespace Nova.CommonUX.Api.Configuration;

/// <summary>
/// Controls the session token backend (2FA sessions and refresh tokens).
/// Stored in <c>opsettings.json → Cache</c>.
/// </summary>
public sealed class CacheProviderSettings
{
    public const string SectionName = "Cache";

    /// <summary>
    /// <c>InMemory</c> — suitable for single-instance and local dev only.
    /// <c>Redis</c> — required for multi-instance deployments.
    /// Default: <c>InMemory</c>.
    /// </summary>
    public string CacheProvider { get; set; } = "InMemory";
}
