namespace Nova.Shared.Migrations;

/// <summary>
/// The outcome of running migrations for one tenant.
/// </summary>
public sealed record MigrationSummary(
    string                    TenantId,
    int                       Applied,
    int                       Blocked,
    IReadOnlyList<BlockedScript> BlockedScripts)
{
    /// <summary>Returns a summary with nothing applied and nothing blocked.</summary>
    public static MigrationSummary Empty(string tenantId) =>
        new(tenantId, Applied: 0, Blocked: 0, []);

    /// <summary>True when at least one script was blocked and logged for manual review.</summary>
    public bool HasBlockedScripts => Blocked > 0;
}

/// <summary>
/// Identifies a migration script that was blocked from automatic execution,
/// together with the human-readable reasons why.
/// </summary>
/// <param name="Name">
/// The script resource name as registered in the DbUp journal
/// (e.g. <c>Nova.Shell.Api.Migrations.MsSql.V003__DropOldColumn.sql</c>).
/// </param>
/// <param name="Reasons">
/// One entry per detected destructive operation, describing the operation type,
/// the SQL line number within the script, and the raw SQL fragment.
/// </param>
public sealed record BlockedScript(
    string               Name,
    IReadOnlyList<string> Reasons);
