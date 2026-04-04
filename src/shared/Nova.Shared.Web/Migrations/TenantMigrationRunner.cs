using System.Reflection;
using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nova.Shared.Configuration;
using Nova.Shared.Migrations;
using Nova.Shared.Security;
using Nova.Shared.Tenancy;
using NovaDbType = Nova.Shared.Data.DbType;

namespace Nova.Shared.Web.Migrations;

/// <summary>
/// Runs pending safe migrations for a single tenant using DbUp.
/// </summary>
/// <remarks>
/// <para><b>Safety pipeline (per script)</b></para>
/// <list type="number">
///   <item>
///     <b>Never-allowed check</b> — <see cref="NeverAllowedDetector"/> scans the full script
///     for <c>DROP DATABASE</c> and <c>DROP SCHEMA</c>. A match blocks the script unconditionally.
///     No config entry can override this.
///   </item>
///   <item>
///     <b>Policy check</b> — <see cref="MigrationPolicyChecker"/> classifies every SQL statement
///     via <see cref="SqlCommandClassifier"/> and checks it against the engine-specific allowlist
///     from <c>migrationpolicy.json → MigrationPolicy.{Engine}</c>.
///     Any unrecognised data command (not in the list) blocks the entire script.
///   </item>
/// </list>
/// Scripts that pass both checks are handed to DbUp and run in their own transaction.
/// Blocked scripts are logged as structured warnings. They remain pending in the DbUp journal
/// and are re-evaluated on every startup until a DBA resolves them manually.
///
/// <para><b>Script layout</b></para>
/// Embedded resources in the calling service assembly:
/// <c>Migrations/{DbType}/V{NNN}__{Description}.sql</c>
/// Scripts execute in alphabetical order.
/// </remarks>
internal sealed class TenantMigrationRunner : IMigrationRunner
{
    private readonly ICipherService                 _cipher;
    private readonly IOptions<AppSettings>          _appOptions;
    private readonly ILogger<TenantMigrationRunner> _logger;

    public TenantMigrationRunner(
        ICipherService                  cipher,
        IOptions<AppSettings>           appOptions,
        ILogger<TenantMigrationRunner>  logger)
    {
        _cipher     = cipher;
        _appOptions = appOptions;
        _logger     = logger;
    }

    // -------------------------------------------------------------------------
    // IMigrationRunner
    // -------------------------------------------------------------------------

    public Task<MigrationSummary> RunAsync(
        TenantRecord      tenant,
        Assembly          scriptsAssembly,
        CancellationToken ct = default)
    {
        return Task.Run(() => Execute(tenant, scriptsAssembly), ct);
    }

    // -------------------------------------------------------------------------
    // Internal
    // -------------------------------------------------------------------------

    private MigrationSummary Execute(TenantRecord tenant, Assembly scriptsAssembly)
    {
        string rawConnStr   = _cipher.Decrypt(tenant.ConnectionString);
        string scriptPrefix = ScriptPrefix(scriptsAssembly, tenant.DbType);

        _logger.LogDebug(
            "Checking migrations for tenant {TenantId} ({DbType})",
            tenant.TenantId, tenant.DbType);

        UpgradeEngine   inspectEngine = BuildEngine(tenant.DbType, rawConnStr, scriptsAssembly, scriptPrefix);
        List<SqlScript> pending       = inspectEngine.GetScriptsToExecute().ToList();

        if (pending.Count == 0)
        {
            _logger.LogDebug("No pending migrations for tenant {TenantId}", tenant.TenantId);
            return MigrationSummary.Empty(tenant.TenantId);
        }

        _logger.LogInformation(
            "Pending migrations for tenant {TenantId}: {Count} script(s)",
            tenant.TenantId, pending.Count);

        // Build the policy checker for this tenant's DB engine.
        MigrationPolicyChecker checker = BuildPolicyChecker(tenant);

        var safeScripts = new List<SqlScript>();
        var blockedList = new List<BlockedScript>();

        foreach (SqlScript script in pending)
        {
            BlockedScript? blocked = EvaluateScript(script, checker);

            if (blocked is not null)
                blockedList.Add(blocked);
            else
                safeScripts.Add(script);
        }

        foreach (BlockedScript b in blockedList)
            LogBlockedScript(tenant.TenantId, b);

        // Run safe scripts.
        int applied = 0;
        if (safeScripts.Count > 0)
        {
            UpgradeEngine         runEngine = BuildEngineWithScripts(tenant.DbType, rawConnStr, safeScripts);
            DatabaseUpgradeResult result    = runEngine.PerformUpgrade();

            if (!result.Successful)
            {
                _logger.LogError(result.Error,
                    "Migration failed for tenant {TenantId} at script {Script}",
                    tenant.TenantId, result.ErrorScript?.Name ?? "unknown");

                throw new InvalidOperationException(
                    $"Migration failed for tenant {tenant.TenantId}" +
                    $" at script '{result.ErrorScript?.Name}': {result.Error?.Message}",
                    result.Error);
            }

            applied = result.Scripts.Count();
            _logger.LogInformation(
                "Migrations applied for tenant {TenantId}: {Count} script(s) — {Names}",
                tenant.TenantId, applied,
                string.Join(", ", result.Scripts.Select(s => s.Name)));
        }

        return new MigrationSummary(
            TenantId:       tenant.TenantId,
            Applied:        applied,
            Blocked:        blockedList.Count,
            BlockedScripts: blockedList);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Safety pipeline
    // ─────────────────────────────────────────────────────────────────────────

    private static BlockedScript? EvaluateScript(
        SqlScript             script,
        MigrationPolicyChecker checker)
    {
        var reasons = new List<string>();

        // Stage 1 — absolute prohibitions (DROP DATABASE, DROP SCHEMA)
        foreach (string violation in NeverAllowedDetector.Detect(script.Contents))
            reasons.Add($"[NEVER-ALLOW] {violation}");

        // Stage 2 — policy allowlist check
        foreach (PolicyViolation v in checker.Check(script.Contents))
            reasons.Add($"[NOT IN POLICY] {v.ToReason()}");

        return reasons.Count > 0
            ? new BlockedScript(script.Name, reasons)
            : null;
    }

    private MigrationPolicyChecker BuildPolicyChecker(TenantRecord tenant)
    {
        MigrationPolicySettings policy = _appOptions.Value.MigrationPolicy;

        (List<string> allowedCommands, string engineName) = tenant.DbType switch
        {
            NovaDbType.MsSql    => (policy.MsSql,    "MsSql"),
            NovaDbType.Postgres => (policy.Postgres,  "Postgres"),
            NovaDbType.MariaDb  => (policy.MariaDb,   "MariaDb"),
            _ => throw new NotSupportedException($"Unknown DbType: {tenant.DbType}")
        };

        if (allowedCommands.Count == 0)
            _logger.LogWarning(
                "Migration policy for {Engine} is empty — all data commands will be blocked.",
                engineName);

        return new MigrationPolicyChecker(allowedCommands, engineName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Logging
    // ─────────────────────────────────────────────────────────────────────────

    private void LogBlockedScript(string tenantId, BlockedScript blocked)
    {
        _logger.LogWarning(
            "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _logger.LogWarning(
            "MIGRATION BLOCKED  Tenant={TenantId}  Script={Script}",
            tenantId, blocked.Name);
        _logger.LogWarning(
            "  {Count} issue(s) prevented automatic execution:", blocked.Reasons.Count);

        foreach (string reason in blocked.Reasons)
            _logger.LogWarning("  • {Reason}", reason);

        _logger.LogWarning(
            "  ACTION REQUIRED: review and run the script manually on the database,");
        _logger.LogWarning(
            "  then journal it so the runner stops flagging it:");
        _logger.LogWarning(
            "  INSERT INTO SchemaVersions (ScriptName, Applied) VALUES ('{Script}', <now>);",
            blocked.Name);
        _logger.LogWarning(
            "  To allow the command to run automatically in future, add it to");
        _logger.LogWarning(
            "  migrationpolicy.json → MigrationPolicy.{Engine}",
            "the appropriate engine");
        _logger.LogWarning(
            "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DbUp engine builders
    // ─────────────────────────────────────────────────────────────────────────

    private UpgradeEngine BuildEngine(
        NovaDbType dbType,
        string     rawConnStr,
        Assembly   assembly,
        string     prefix)
    {
        IUpgradeLog log = new DbUpLogAdapter(_logger);

        return dbType switch
        {
            NovaDbType.MsSql => DeployChanges.To
                .SqlDatabase(rawConnStr)
                .WithScriptsEmbeddedInAssembly(assembly, s => s.StartsWith(prefix))
                .WithTransactionPerScript()
                .LogTo(log)
                .Build(),

            NovaDbType.Postgres => DeployChanges.To
                .PostgresqlDatabase(rawConnStr)
                .WithScriptsEmbeddedInAssembly(assembly, s => s.StartsWith(prefix))
                .WithTransactionPerScript()
                .LogTo(log)
                .Build(),

            NovaDbType.MariaDb => DeployChanges.To
                .MySqlDatabase(rawConnStr)
                .WithScriptsEmbeddedInAssembly(assembly, s => s.StartsWith(prefix))
                .WithTransactionPerScript()
                .LogTo(log)
                .Build(),

            _ => throw new NotSupportedException($"Unsupported DbType: {dbType}")
        };
    }

    private UpgradeEngine BuildEngineWithScripts(
        NovaDbType             dbType,
        string                 rawConnStr,
        IEnumerable<SqlScript> scripts)
    {
        IUpgradeLog log = new DbUpLogAdapter(_logger);

        return dbType switch
        {
            NovaDbType.MsSql => DeployChanges.To
                .SqlDatabase(rawConnStr)
                .WithScripts(scripts)
                .WithTransactionPerScript()
                .LogTo(log)
                .Build(),

            NovaDbType.Postgres => DeployChanges.To
                .PostgresqlDatabase(rawConnStr)
                .WithScripts(scripts)
                .WithTransactionPerScript()
                .LogTo(log)
                .Build(),

            NovaDbType.MariaDb => DeployChanges.To
                .MySqlDatabase(rawConnStr)
                .WithScripts(scripts)
                .WithTransactionPerScript()
                .LogTo(log)
                .Build(),

            _ => throw new NotSupportedException($"Unsupported DbType: {dbType}")
        };
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static string ScriptPrefix(Assembly assembly, NovaDbType dbType) =>
        $"{assembly.GetName().Name}.Migrations.{dbType}.";

    private sealed class DbUpLogAdapter : IUpgradeLog
    {
        private readonly ILogger _logger;
        public DbUpLogAdapter(ILogger logger) => _logger = logger;

        public void WriteInformation(string format, params object[] args) =>
            _logger.LogDebug(format, args);

        public void WriteWarning(string format, params object[] args) =>
            _logger.LogWarning(format, args);

        public void WriteError(string format, params object[] args) =>
            _logger.LogError(format, args);
    }
}
