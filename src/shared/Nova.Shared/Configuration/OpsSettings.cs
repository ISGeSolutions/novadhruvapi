using Nova.Shared.Caching;
using Nova.Shared.Logging;

namespace Nova.Shared.Configuration;

/// <summary>Strongly-typed root for <c>opsettings.json</c> (hot-reloadable operational config).</summary>
public sealed class OpsSettings
{
    /// <summary>Logging configuration.</summary>
    public OpsLoggingSettings Logging { get; set; } = new();

    /// <summary>Caching configuration.</summary>
    public CacheSettings Caching { get; set; } = new();

    /// <summary>Per-tenant rate limiting configuration.</summary>
    public OpsRateLimitingSettings RateLimiting { get; set; } = new();

    /// <summary>Outbox relay configuration.</summary>
    public OutboxRelaySettings OutboxRelay { get; set; } = new();
}


/// <summary>
/// Per-tenant rate limiting settings (hot-reloadable).
/// <para>
/// <see cref="Enabled"/> is checked on every request — toggling it takes effect immediately
/// without a restart. <see cref="PermitLimit"/>, <see cref="WindowSeconds"/>, and
/// <see cref="QueueLimit"/> are read per-request from the current <c>opsettings.json</c> values,
/// so changes to these limits also take effect without a restart.
/// </para>
/// </summary>
public sealed class OpsRateLimitingSettings
{
    /// <summary>
    /// When <c>false</c>, rate limiting is bypassed for all requests.
    /// Use for emergency disable without a deployment.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum number of requests allowed per <see cref="WindowSeconds"/> per tenant (or IP for anonymous).</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Duration of each rate limit window in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Number of requests to queue when the limit is reached before rejecting.
    /// Set to <c>0</c> to reject immediately (recommended for APIs).
    /// </summary>
    public int QueueLimit { get; set; } = 0;
}

/// <summary>
/// Outbox relay operational settings (hot-reloadable).
/// Changes to <see cref="PollingIntervalSeconds"/> and <see cref="BatchSize"/> take effect
/// on the next polling cycle without a restart.
/// </summary>
public sealed class OutboxRelaySettings
{
    /// <summary>
    /// When <c>false</c>, the relay worker skips all tenants.
    /// Use for emergency disable without a deployment.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the relay polls for pending messages, in seconds. Default: 5.</summary>
    public int PollingIntervalSeconds { get; set; } = 5;

    /// <summary>Maximum number of messages fetched per tenant per polling cycle. Default: 50.</summary>
    public int BatchSize { get; set; } = 50;
}

/// <summary>Operational logging settings.</summary>
public sealed class OpsLoggingSettings
{
    /// <summary>Default minimum log level.</summary>
    public string DefaultLevel { get; set; } = "Information";

    /// <summary>When true, request/response bodies are included in logs.</summary>
    public bool EnableRequestResponseLogging { get; set; }

    /// <summary>When true, the debug file sink is active.</summary>
    public bool EnableDiagnosticLogging { get; set; }

    /// <summary>Time-window overrides for log level.</summary>
    public List<LoggingWindow> Windows { get; set; } = [];
}
