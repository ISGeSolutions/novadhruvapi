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

    /// <summary>Health check overrides.</summary>
    public OpsHealthCheckSettings HealthChecks { get; set; } = new();
}

/// <summary>Health check suppression overrides (hot-reloadable).</summary>
public sealed class OpsHealthCheckSettings
{
    /// <summary>When true, the PostgreSQL health check returns Degraded with a suppressed message instead of hitting the DB.</summary>
    public bool DisablePostgres { get; set; }

    /// <summary>When true, the MSSQL health check returns Degraded with a suppressed message instead of hitting the DB.</summary>
    public bool DisableMsSql { get; set; }

    /// <summary>When true, the MariaDB health check returns Degraded with a suppressed message instead of hitting the DB.</summary>
    public bool DisableMariaDb { get; set; }
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
