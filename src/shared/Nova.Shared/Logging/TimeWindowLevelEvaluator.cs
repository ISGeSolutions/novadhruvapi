using Nova.Shared.Configuration;
using Serilog.Events;

namespace Nova.Shared.Logging;

/// <summary>
/// Evaluates the current UTC time against configured logging windows and returns
/// the appropriate <see cref="LogEventLevel"/>.
/// </summary>
public static class TimeWindowLevelEvaluator
{
    /// <summary>
    /// Returns the effective log level for the current UTC time,
    /// falling back to the <see cref="OpsLoggingSettings.DefaultLevel"/> if no window matches.
    /// </summary>
    public static LogEventLevel Evaluate(OpsLoggingSettings settings)
    {
        TimeOnly now = TimeOnly.FromDateTime(DateTime.UtcNow);

        foreach (LoggingWindow window in settings.Windows)
        {
            if (!TimeOnly.TryParseExact(window.Start, "HH:mm", out TimeOnly start)) continue;
            if (!TimeOnly.TryParseExact(window.End, "HH:mm", out TimeOnly end)) continue;

            bool inWindow = start <= end
                ? now >= start && now < end
                : now >= start || now < end; // overnight window

            if (inWindow)
                return ParseLevel(window.Level);
        }

        return ParseLevel(settings.DefaultLevel);
    }

    private static LogEventLevel ParseLevel(string level) =>
        Enum.TryParse<LogEventLevel>(level, ignoreCase: true, out LogEventLevel result)
            ? result
            : LogEventLevel.Information;
}
