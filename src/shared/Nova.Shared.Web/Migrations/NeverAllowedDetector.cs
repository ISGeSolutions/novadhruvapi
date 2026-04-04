using System.Text.RegularExpressions;

namespace Nova.Shared.Web.Migrations;

/// <summary>
/// Detects SQL operations that are <em>unconditionally</em> prohibited from executing
/// automatically, regardless of database state.
/// </summary>
/// <remarks>
/// These patterns represent catastrophic, tenant-wide or instance-wide destructive operations.
/// No runtime check (table emptiness, column nullability, etc.) can make them safe to run
/// automatically. They must always be reviewed and run manually by a DBA.
///
/// <para><b>Currently blocked</b></para>
/// <list type="bullet">
///   <item><c>DROP DATABASE</c> — destroys the entire tenant database</item>
///   <item><c>DROP SCHEMA</c> — destroys an entire schema and all objects within it</item>
/// </list>
///
/// All other commands (including <c>TRUNCATE</c>, <c>DROP TABLE</c>, etc.) are governed
/// by the per-engine allowlist in <c>migrationpolicy.json</c>.
///
/// These are enforced by the migration runner before the policy check is performed.
/// </remarks>
internal static class NeverAllowedDetector
{
    private static readonly IReadOnlyList<(Regex Pattern, string Label)> Patterns =
    [
        (new Regex(@"\bDROP\s+DATABASE\b",   RegexOptions.IgnoreCase | RegexOptions.Compiled), "DROP DATABASE"),
        (new Regex(@"\bDROP\s+SCHEMA\b",     RegexOptions.IgnoreCase | RegexOptions.Compiled), "DROP SCHEMA"),
    ];

    /// <summary>
    /// Returns a list of "never-allowed" violation descriptions found in <paramref name="sql"/>.
    /// An empty list means no absolute prohibitions were found.
    /// </summary>
    /// <remarks>
    /// This check runs on the full script text before statement-level detection.
    /// Even a single match blocks the entire script with no conditional escape.
    /// </remarks>
    public static IReadOnlyList<string> Detect(string sql)
    {
        // Strip single-line comments only — we want to catch DROP DATABASE even inside
        // a multi-line comment that someone left uncommented by mistake.
        string cleaned = Regex.Replace(sql, @"--[^\n]*", "");

        var violations = new List<string>();

        foreach ((Regex pattern, string label) in Patterns)
        {
            Match m = pattern.Match(cleaned);
            if (m.Success)
            {
                int line = cleaned[..m.Index].Count(c => c == '\n') + 1;
                violations.Add($"Line {line}: {label} — prohibited unconditionally");
            }
        }

        return violations;
    }
}
