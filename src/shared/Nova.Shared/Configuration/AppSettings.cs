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

    /// <summary>Service-to-service internal authentication settings.</summary>
    public InternalAuthSettings InternalAuth { get; set; } = new();

    /// <summary>OpenTelemetry settings.</summary>
    public OpenTelemetrySettings OpenTelemetry { get; set; } = new();

    /// <summary>
    /// Per-engine allowlist of SQL commands permitted to execute automatically during migrations.
    /// Loaded from <c>migrationpolicy.json</c>.
    /// </summary>
    public MigrationPolicySettings MigrationPolicy { get; set; } = new();

    /// <summary>RabbitMQ connection settings. Used by the outbox relay for RabbitMq tenants.</summary>
    public RabbitMqSettings RabbitMq { get; set; } = new();
}

/// <summary>
/// Holds per-engine diagnostic connection entries.
/// Each entry carries an encrypted connection string and an <see cref="DiagnosticConnectionEntry.Enabled"/> flag.
/// When <c>Enabled</c> is <c>false</c> the console-mode connectivity ping and the
/// <c>/test-db/*</c> endpoints skip that engine entirely.
/// </summary>
public sealed class DiagnosticConnectionSettings
{
    public DiagnosticConnectionEntry MsSql    { get; set; } = new();
    public DiagnosticConnectionEntry Postgres { get; set; } = new();
    public DiagnosticConnectionEntry MariaDb  { get; set; } = new();
}

/// <summary>A single diagnostic connection entry.</summary>
public sealed class DiagnosticConnectionEntry
{
    /// <summary>Encrypted connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// When <c>false</c>, the console-mode ping and diagnostic endpoint skip this engine.
    /// Default: <c>false</c> — opt-in per environment.
    /// </summary>
    public bool Enabled { get; set; } = false;
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

/// <summary>
/// Settings for service-to-service internal JWT authentication.
/// Stored in <c>appsettings.json → InternalAuth</c>.
/// </summary>
public sealed class InternalAuthSettings
{
    /// <summary>
    /// Logical name of this service (e.g. <c>nova-shell</c>).
    /// Used as the <c>sub</c> claim in outbound internal tokens.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Encrypted symmetric signing key shared by all Nova services.
    /// Decrypted at runtime by <c>ICipherService</c>.
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Lifetime in seconds for generated outbound tokens. Default: 300 (5 minutes).
    /// Tokens are renewed 30 seconds before expiry.
    /// </summary>
    public int TokenLifetimeSeconds { get; set; } = 300;
}

/// <summary>
/// Per-engine allowlists of SQL commands that the migration runner is permitted to execute
/// automatically. Loaded from <c>migrationpolicy.json → MigrationPolicy</c>.
///
/// <para><b>Command names</b></para>
/// Each entry is the canonical command string matched by <c>SqlCommandClassifier</c>:
/// <list type="bullet">
///   <item><c>CREATE TABLE</c>, <c>CREATE INDEX</c>, <c>CREATE VIEW</c></item>
///   <item><c>DROP TABLE</c>, <c>DROP INDEX</c>, <c>DROP VIEW</c></item>
///   <item><c>ALTER TABLE ADD</c> — ADD COLUMN or ADD CONSTRAINT</item>
///   <item><c>ALTER TABLE DROP</c> — DROP COLUMN or DROP CONSTRAINT</item>
///   <item><c>ALTER TABLE ALTER</c> — ALTER COLUMN (MSSQL)</item>
///   <item><c>ALTER TABLE MODIFY</c> — MODIFY COLUMN (MySQL/MariaDB)</item>
///   <item><c>ALTER TABLE CHANGE</c> — CHANGE COLUMN (MySQL/MariaDB)</item>
///   <item><c>ALTER TABLE SET</c> — SET DEFAULT / SET NOT NULL (Postgres)</item>
///   <item><c>INSERT</c>, <c>UPDATE</c>, <c>DELETE</c>, <c>SELECT</c></item>
///   <item><c>TRUNCATE</c>, <c>MERGE</c>, <c>RENAME TABLE</c></item>
/// </list>
///
/// Commands absent from the list cause the entire script to be blocked and logged.
/// Non-data utility statements (PRINT, SET, DECLARE, comments) are never classified as
/// data commands and always pass regardless of this list.
///
/// <para><b>Unconditional prohibitions (override this list)</b></para>
/// <c>DROP DATABASE</c> and <c>DROP SCHEMA</c> are always blocked by
/// <c>NeverAllowedDetector</c> regardless of what appears in this list.
/// </summary>
public sealed class MigrationPolicySettings
{
    /// <summary>Allowed SQL commands for MSSQL tenant databases.</summary>
    public List<string> MsSql    { get; set; } = [];

    /// <summary>Allowed SQL commands for PostgreSQL tenant databases.</summary>
    public List<string> Postgres { get; set; } = [];

    /// <summary>Allowed SQL commands for MariaDB / MySQL tenant databases.</summary>
    public List<string> MariaDb  { get; set; } = [];
}

/// <summary>
/// RabbitMQ connection settings.
/// Stored in <c>appsettings.json → RabbitMq</c>.
/// </summary>
public sealed class RabbitMqSettings
{
    /// <summary>RabbitMQ broker hostname.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>AMQP port. Default: 5672.</summary>
    public int Port { get; set; } = 5672;

    /// <summary>RabbitMQ username.</summary>
    public string Username { get; set; } = "guest";

    /// <summary>
    /// Encrypted RabbitMQ password. Decrypted at runtime by <see cref="Nova.Shared.Security.ICipherService"/>.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>RabbitMQ virtual host. Default: <c>/</c>.</summary>
    public string VirtualHost { get; set; } = "/";
}

/// <summary>OpenTelemetry exporter settings.</summary>
public sealed class OpenTelemetrySettings
{
    /// <summary>Logical service name sent with all telemetry.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>OTLP exporter endpoint (e.g. Datadog agent).</summary>
    public string OtlpEndpoint { get; set; } = string.Empty;
}
