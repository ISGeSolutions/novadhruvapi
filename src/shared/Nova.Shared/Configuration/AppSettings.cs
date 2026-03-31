using Nova.Shared.Tenancy;

namespace Nova.Shared.Configuration;

/// <summary>Strongly-typed root for <c>appsettings.json</c>.</summary>
public sealed class AppSettings
{
    /// <summary>Named diagnostic connection strings (not tenant-specific).</summary>
    public DiagnosticConnectionSettings DiagnosticConnections { get; set; } = new();

    /// <summary>All registered tenants.</summary>
    public List<TenantRecord> Tenants { get; set; } = [];

    /// <summary>JWT authentication settings.</summary>
    public JwtSettings Jwt { get; set; } = new();

    /// <summary>OpenTelemetry settings.</summary>
    public OpenTelemetrySettings OpenTelemetry { get; set; } = new();
}

/// <summary>Holds encrypted connection strings used for diagnostic endpoints.</summary>
public sealed class DiagnosticConnectionSettings
{
    /// <summary>Encrypted MSSQL connection string for diagnostics.</summary>
    public string MsSql { get; set; } = string.Empty;

    /// <summary>Encrypted PostgreSQL connection string for diagnostics.</summary>
    public string Postgres { get; set; } = string.Empty;

    /// <summary>Encrypted MariaDB connection string for diagnostics.</summary>
    public string MariaDb { get; set; } = string.Empty;
}

/// <summary>JWT bearer authentication settings.</summary>
public sealed class JwtSettings
{
    /// <summary>Expected token issuer.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Expected token audience.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Encrypted signing secret key.</summary>
    public string SecretKey { get; set; } = string.Empty;
}

/// <summary>OpenTelemetry exporter settings.</summary>
public sealed class OpenTelemetrySettings
{
    /// <summary>Logical service name sent with all telemetry.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>OTLP exporter endpoint (e.g. Datadog agent).</summary>
    public string OtlpEndpoint { get; set; } = string.Empty;
}
