using System.Collections.Concurrent;

namespace Nova.CommonUX.Api.Services;

/// <summary>
/// In-process <see cref="ISessionStore"/> backed by a <see cref="ConcurrentDictionary"/>.
/// Suitable for single-instance deployments and local dev only.
/// Registered when <c>opsettings.json → Cache.CacheProvider = "InMemory"</c>.
/// </summary>
internal sealed class InMemorySessionStore : ISessionStore
{
    private sealed record Entry(string Value, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _store = new();

    public Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken ct = default)
    {
        _store[key] = new Entry(value, DateTimeOffset.UtcNow.Add(expiry));
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (_store.TryGetValue(key, out Entry? entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
            return Task.FromResult<string?>(entry.Value);

        _store.TryRemove(key, out _);
        return Task.FromResult<string?>(null);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task DeleteByPatternAsync(string pattern, CancellationToken ct = default)
    {
        // Convert Redis wildcard pattern (e.g. "refresh:T001:U001:*") to a string prefix match.
        // Only '*' at the end is supported — sufficient for the refresh token revocation use case.
        string prefix = pattern.TrimEnd('*');
        var toRemove  = _store.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (string key in toRemove)
            _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
