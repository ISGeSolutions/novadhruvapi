using System.Text.RegularExpressions;

namespace Nova.Shared.Web.Migrations;

/// <summary>
/// Checks every SQL statement in a migration script against the per-engine
/// command allowlist from <c>migrationpolicy.json</c>.
/// </summary>
/// <remarks>
/// <para><b>Logic</b></para>
/// For each statement in the script (split on <c>;</c> / <c>GO</c>):
/// <list type="number">
///   <item><see cref="SqlCommandClassifier.Classify"/> identifies the command.</item>
///   <item>If classification returns <c>null</c> (empty, comment, utility statement like
///         PRINT or SET) — the statement is always allowed.</item>
///   <item>If the classified command is in the allowlist — allowed.</item>
///   <item>If the classified command is NOT in the allowlist — a
///         <see cref="PolicyViolation"/> is recorded and the entire script is blocked.</item>
/// </list>
///
/// <para><b>Case-insensitive matching</b></para>
/// Both the allowlist entries and the classified command are normalised to upper-case
/// before comparison, so <c>"create table"</c> and <c>"CREATE TABLE"</c> are equivalent.
/// </remarks>
internal sealed class MigrationPolicyChecker
{
    private readonly IReadOnlySet<string> _allowed;
    private readonly string               _engineName;

    /// <param name="allowedCommands">
    /// The list from <c>migrationpolicy.json → MigrationPolicy.{Engine}</c>.
    /// An empty list blocks every data-affecting command.
    /// </param>
    /// <param name="engineName">Display name used in log messages (e.g. <c>"MsSql"</c>).</param>
    public MigrationPolicyChecker(IEnumerable<string> allowedCommands, string engineName)
    {
        _allowed    = new HashSet<string>(
            allowedCommands.Select(c => c.Trim().ToUpperInvariant()),
            StringComparer.OrdinalIgnoreCase);
        _engineName = engineName;
    }

    /// <summary>
    /// Checks all statements in <paramref name="scriptContents"/> against the allowlist.
    /// Returns one <see cref="PolicyViolation"/> per blocked statement.
    /// An empty list means the script is fully allowed.
    /// </summary>
    public IReadOnlyList<PolicyViolation> Check(string scriptContents)
    {
        string   cleaned    = StripComments(scriptContents);
        string[] statements = SplitStatements(cleaned);
        var      violations = new List<PolicyViolation>();
        int      lineOffset = 0;

        foreach (string statement in statements)
        {
            string? command = SqlCommandClassifier.Classify(statement);

            if (command is not null && !_allowed.Contains(command.ToUpperInvariant()))
            {
                violations.Add(new PolicyViolation(
                    Command:         command,
                    LineNumber:      lineOffset + 1,
                    StatementPreview: FirstLine(statement.Trim()),
                    Engine:          _engineName));
            }

            lineOffset += statement.Count(c => c == '\n');
        }

        return violations;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static string StripComments(string sql)
    {
        string result = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        result = Regex.Replace(result, @"--[^\n]*", "");
        return result;
    }

    private static string[] SplitStatements(string sql)
    {
        // Normalise MSSQL GO batch separators to semicolons, then split uniformly.
        string normalized = Regex.Replace(
            sql, @"^\s*GO\s*$", ";", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return normalized.Split(';');
    }

    private static string FirstLine(string text)
    {
        int i = text.IndexOf('\n');
        return (i >= 0 ? text[..i] : text).Trim();
    }
}

/// <summary>A single statement that is not in the engine's allowed command list.</summary>
/// <param name="Command">Canonical command name (e.g. <c>"DROP TABLE"</c>).</param>
/// <param name="LineNumber">Approximate 1-based line in the script where the statement starts.</param>
/// <param name="StatementPreview">First line of the statement (trimmed) for log context.</param>
/// <param name="Engine">DB engine name for the log message.</param>
public sealed record PolicyViolation(
    string Command,
    int    LineNumber,
    string StatementPreview,
    string Engine)
{
    /// <summary>Human-readable reason string for the blocked-script log entry.</summary>
    public string ToReason() =>
        $"Line {LineNumber}: '{Command}' is not in the {Engine} migration policy — {StatementPreview}";
}
