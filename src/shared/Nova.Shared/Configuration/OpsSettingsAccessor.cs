namespace Nova.Shared.Configuration;

/// <summary>
/// Wraps <see cref="OpsSettingsWatcher"/> to expose the last known good
/// <see cref="OpsSettings"/> via <see cref="IOpsSettingsAccessor"/>.
/// </summary>
public sealed class OpsSettingsAccessor : IOpsSettingsAccessor
{
    private readonly OpsSettingsWatcher _watcher;

    /// <summary>Initialises the accessor.</summary>
    public OpsSettingsAccessor(OpsSettingsWatcher watcher)
    {
        _watcher = watcher;
    }

    /// <inheritdoc />
    public OpsSettings Current => _watcher.Current;
}
