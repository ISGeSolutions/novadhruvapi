namespace Nova.Shared.Caching;

/// <summary>Defines caching behaviour for a named profile.</summary>
public sealed record CacheProfile
{
    /// <summary>Cache storage layer: <c>Redis</c>, <c>Memory</c>, or <c>None</c>.</summary>
    public string Layer { get; init; } = "None";

    /// <summary>Time-to-live in seconds.</summary>
    public int TtlSeconds { get; init; }

    /// <summary>Whether caching is enabled for this profile.</summary>
    public bool Enabled { get; init; }
}
