namespace Nova.ToDo.Api.Configuration;

/// <summary>
/// Hot-reloadable concurrency check settings, bound from <c>opsettings.json → ConcurrencyCheck</c>.
/// Inject via <see cref="Microsoft.Extensions.Options.IOptionsSnapshot{T}"/> in endpoints that perform mutations.
/// </summary>
public sealed class ConcurrencySettings
{
    public const string SectionName = "ConcurrencyCheck";

    /// <summary>
    /// When true, any mutation whose <c>updated_on</c> is earlier than the DB value returns 409.
    /// Toggle false in opsettings.json to suppress conflict checks without redeploying.
    /// </summary>
    public bool StrictMode { get; init; } = true;

    /// <summary>Message returned in the 409 response body.</summary>
    public string ConflictMessage { get; init; } = "Record was updated between your read and update. Refresh data.";
}
