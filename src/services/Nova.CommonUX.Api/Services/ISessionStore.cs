namespace Nova.CommonUX.Api.Services;

/// <summary>
/// Stores short-lived session tokens for 2FA flows and rotating refresh tokens.
/// Backed by Redis (multi-instance) or in-process ConcurrentDictionary (single-instance/dev).
/// Controlled by <c>opsettings.json → Cache.CacheProvider</c>.
/// </summary>
public interface ISessionStore
{
    /// <summary>Stores a value under <paramref name="key"/> with the given expiry.</summary>
    Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken ct = default);

    /// <summary>Returns the stored value for <paramref name="key"/>, or <c>null</c> if absent or expired.</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Deletes the entry for <paramref name="key"/>.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Deletes all keys that match <paramref name="pattern"/> (Redis SCAN wildcard syntax).
    /// Used to revoke all refresh tokens for a user on password reset.
    /// </summary>
    Task DeleteByPatternAsync(string pattern, CancellationToken ct = default);
}
