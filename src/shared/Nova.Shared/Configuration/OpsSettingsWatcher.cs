using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nova.Shared.Configuration;

/// <summary>
/// Hosted service that monitors <see cref="OpsSettings"/> for changes and validates
/// before applying, retaining the last known good configuration on failure.
/// </summary>
public sealed class OpsSettingsWatcher : IHostedService, IDisposable
{
    private readonly IOptionsMonitor<OpsSettings> _monitor;
    private readonly OpsSettingsValidator _validator;
    private readonly ILogger<OpsSettingsWatcher> _logger;
    private IDisposable? _changeToken;

    /// <summary>The last successfully validated <see cref="OpsSettings"/> instance.</summary>
    public OpsSettings Current { get; private set; }

    /// <summary>Initialises the watcher with the initial settings.</summary>
    public OpsSettingsWatcher(
        IOptionsMonitor<OpsSettings> monitor,
        OpsSettingsValidator validator,
        ILogger<OpsSettingsWatcher> logger)
    {
        _monitor = monitor;
        _validator = validator;
        _logger = logger;
        Current = monitor.CurrentValue;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _changeToken = _monitor.OnChange(OnSettingsChanged);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _changeToken?.Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _changeToken?.Dispose();

    private void OnSettingsChanged(OpsSettings newSettings)
    {
        ValidateOptionsResult result = _validator.Validate(null, newSettings);
        if (result.Succeeded)
        {
            Current = newSettings;
            _logger.LogInformation("[OpsSettings] Reloaded successfully.");
        }
        else
        {
            _logger.LogWarning(
                "[OpsSettings] Validation failed — retaining previous config: {Errors}",
                string.Join("; ", result.Failures ?? []));
        }
    }
}
