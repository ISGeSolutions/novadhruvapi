using Microsoft.Extensions.Options;
using Nova.Shared.Logging;

namespace Nova.Shared.Configuration;

/// <summary>Validates <see cref="OpsSettings"/> before it is applied on hot-reload.</summary>
public sealed class OpsSettingsValidator : IValidateOptions<OpsSettings>
{
    private static readonly HashSet<string> ValidLogLevels =
        ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpsSettings options)
    {
        List<string> errors = [];

        if (!ValidLogLevels.Contains(options.Logging.DefaultLevel))
            errors.Add($"Logging.DefaultLevel '{options.Logging.DefaultLevel}' is not a valid Serilog log level.");

        foreach (LoggingWindow window in options.Logging.Windows)
        {
            if (string.IsNullOrWhiteSpace(window.Name))
                errors.Add("A logging window is missing a Name.");

            if (!TimeOnly.TryParseExact(window.Start, "HH:mm", out _))
                errors.Add($"Logging window '{window.Name}': Start '{window.Start}' is not a valid HH:mm time.");

            if (!TimeOnly.TryParseExact(window.End, "HH:mm", out _))
                errors.Add($"Logging window '{window.Name}': End '{window.End}' is not a valid HH:mm time.");

            if (!ValidLogLevels.Contains(window.Level))
                errors.Add($"Logging window '{window.Name}': Level '{window.Level}' is not valid.");
        }

        if (options.RateLimiting.PermitLimit < 1)
            errors.Add($"RateLimiting.PermitLimit must be >= 1 (got {options.RateLimiting.PermitLimit}).");

        if (options.RateLimiting.WindowSeconds < 1)
            errors.Add($"RateLimiting.WindowSeconds must be >= 1 (got {options.RateLimiting.WindowSeconds}).");

        if (options.RateLimiting.QueueLimit < 0)
            errors.Add($"RateLimiting.QueueLimit must be >= 0 (got {options.RateLimiting.QueueLimit}).");

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
