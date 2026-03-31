namespace Nova.Shared.Configuration;

/// <summary>Provides access to the last known good <see cref="OpsSettings"/> instance.</summary>
public interface IOpsSettingsAccessor
{
    /// <summary>The current validated operational settings.</summary>
    OpsSettings Current { get; }
}
