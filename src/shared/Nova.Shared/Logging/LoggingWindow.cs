namespace Nova.Shared.Logging;

/// <summary>Defines a time window during which a specific log level override is active.</summary>
public sealed record LoggingWindow
{
    /// <summary>Descriptive name for the window (e.g. <c>PeakMorning</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Window start time in <c>HH:mm</c> format (UTC).</summary>
    public required string Start { get; init; }

    /// <summary>Window end time in <c>HH:mm</c> format (UTC).</summary>
    public required string End { get; init; }

    /// <summary>Serilog level name active during this window (e.g. <c>Warning</c>).</summary>
    public required string Level { get; init; }
}
