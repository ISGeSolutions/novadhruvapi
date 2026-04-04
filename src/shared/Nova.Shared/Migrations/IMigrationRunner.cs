using System.Reflection;
using Nova.Shared.Tenancy;

namespace Nova.Shared.Migrations;

/// <summary>
/// Runs pending database migrations for a single tenant.
/// </summary>
/// <remarks>
/// SQL migration scripts are embedded resources in the calling service assembly, organised under
/// <c>Migrations/{DbType}/</c> (e.g. <c>Migrations/MsSql/V001__Initial.sql</c>).
///
/// <para><b>Safe vs. blocked scripts</b></para>
/// Scripts containing destructive operations (DROP TABLE, DROP COLUMN, ALTER COLUMN,
/// MODIFY COLUMN, CHANGE COLUMN) are never executed automatically. They are logged as
/// structured warnings and omitted from the run. All other pending scripts execute normally.
///
/// <para><b>Resuming after a blocked script</b></para>
/// A blocked script remains "pending" in DbUp's journal until a DBA:
/// <list type="number">
///   <item>Reviews the logged warning.</item>
///   <item>Runs the script manually on the target database.</item>
///   <item>Inserts a row into the <c>SchemaVersions</c> journal table so the runner
///         recognises the script as applied:
///         <code>INSERT INTO SchemaVersions (ScriptName, Applied) VALUES ('V003__...sql', GETDATE())</code>
///   </item>
/// </list>
/// </remarks>
public interface IMigrationRunner
{
    /// <summary>
    /// Runs all pending safe migrations for <paramref name="tenant"/>.
    /// </summary>
    /// <param name="tenant">The tenant whose database will be migrated.</param>
    /// <param name="scriptsAssembly">
    /// Assembly containing embedded SQL scripts under <c>Migrations/{DbType}/</c>.
    /// Typically <c>typeof(Program).Assembly</c> from the calling service.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// A <see cref="MigrationSummary"/> with counts of applied and blocked scripts.
    /// Blocked scripts do not cause an exception — they appear in the summary and as log warnings.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a safe script fails to execute (genuine migration error, not a safety block).
    /// </exception>
    Task<MigrationSummary> RunAsync(
        TenantRecord tenant,
        Assembly     scriptsAssembly,
        CancellationToken ct = default);
}
