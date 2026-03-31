namespace Nova.Shared.Caching;

/// <summary>Defines cache get-or-set and invalidation operations.</summary>
public interface ICacheService
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/> if present,
    /// otherwise invokes <paramref name="factory"/>, caches the result, and returns it.
    /// </summary>
    Task<T?> GetOrSetAsync<T>(string key, Func<Task<T>> factory, string profileName, CancellationToken cancellationToken = default);

    /// <summary>Removes the cached value for <paramref name="key"/>.</summary>
    Task InvalidateAsync(string key, CancellationToken cancellationToken = default);
}
