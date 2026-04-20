using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nova.Shared.Configuration;
using Serilog.Core;

namespace Nova.Shared.Logging;

/// <summary>
/// Hosted service that keeps the Serilog <see cref="LoggingLevelSwitch"/> in sync with
/// <c>opsettings.json</c>. Without this, the level switch is set once at startup and hot-reloading
/// <c>Logging.DefaultLevel</c> (or time-window levels) has no effect until the service restarts.
/// </summary>
public sealed class LogLevelSynchroniser : IHostedService, IDisposable
{
    private readonly IOptionsMonitor<OpsSettings> _opsMonitor;
    private readonly LoggingLevelSwitch           _levelSwitch;
    private IDisposable?                          _changeToken;

    /// <summary>Initialises the synchroniser.</summary>
    public LogLevelSynchroniser(
        IOptionsMonitor<OpsSettings> opsMonitor,
        LoggingLevelSwitch           levelSwitch)
    {
        _opsMonitor  = opsMonitor;
        _levelSwitch = levelSwitch;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _changeToken = _opsMonitor.OnChange(ops =>
            _levelSwitch.MinimumLevel = TimeWindowLevelEvaluator.Evaluate(ops.Logging));

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
}
