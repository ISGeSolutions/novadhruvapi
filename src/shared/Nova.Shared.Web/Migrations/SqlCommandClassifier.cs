using System.Text.RegularExpressions;

namespace Nova.Shared.Web.Migrations;

/// <summary>
/// Extracts the canonical SQL command name from a single SQL statement.
/// </summary>
/// <remarks>
/// Returns a string like <c>"CREATE TABLE"</c>, <c>"ALTER TABLE ADD"</c>, <c>"INSERT"</c>
/// that matches the entries in <c>migrationpolicy.json → MigrationPolicy.{Engine}</c>.
///
/// Returns <c>null</c> for:
/// <list type="bullet">
///   <item>Empty or comment-only statements</item>
///   <item>Non-data utility statements: PRINT, SET, DECLARE, EXEC (non-DML), GO, USE, etc.</item>
/// </list>
/// A <c>null</c> result means "not a data-affecting command — always allow".
///
/// <para><b>Canonical command names produced</b></para>
/// <list type="bullet">
///   <item><c>CREATE TABLE</c>, <c>CREATE INDEX</c>, <c>CREATE VIEW</c>,
///         <c>CREATE PROCEDURE</c>, <c>CREATE FUNCTION</c>, <c>CREATE TRIGGER</c>,
///         <c>CREATE SEQUENCE</c>, <c>CREATE TYPE</c></item>
///   <item><c>DROP TABLE</c>, <c>DROP INDEX</c>, <c>DROP VIEW</c>,
///         <c>DROP PROCEDURE</c>, <c>DROP FUNCTION</c>, <c>DROP TRIGGER</c></item>
///   <item><c>ALTER TABLE ADD</c> — ADD COLUMN / ADD CONSTRAINT / ADD INDEX</item>
///   <item><c>ALTER TABLE DROP</c> — DROP COLUMN / DROP CONSTRAINT</item>
///   <item><c>ALTER TABLE ALTER</c> — ALTER COLUMN (MSSQL / Postgres)</item>
///   <item><c>ALTER TABLE MODIFY</c> — MODIFY [COLUMN] (MySQL / MariaDB)</item>
///   <item><c>ALTER TABLE CHANGE</c> — CHANGE [COLUMN] (MySQL / MariaDB)</item>
///   <item><c>ALTER TABLE RENAME</c> — RENAME COLUMN / RENAME TO</item>
///   <item><c>ALTER TABLE SET</c> — SET DEFAULT / SET NOT NULL (Postgres)</item>
///   <item><c>ALTER TABLE ENABLE</c> / <c>ALTER TABLE DISABLE</c> — constraints</item>
///   <item><c>ALTER TABLE</c> — any other ALTER TABLE sub-command</item>
///   <item><c>INSERT</c>, <c>UPDATE</c>, <c>DELETE</c>, <c>SELECT</c></item>
///   <item><c>TRUNCATE</c>, <c>MERGE</c>, <c>REPLACE</c>, <c>RENAME TABLE</c></item>
/// </list>
/// </remarks>
internal static class SqlCommandClassifier
{
    // CREATE [OR REPLACE] [UNIQUE] <object-type>
    // Handles: CREATE TABLE, CREATE UNIQUE INDEX, CREATE OR REPLACE VIEW, etc.
    private static readonly Regex CreatePattern = new(
        @"^\s*CREATE\s+(?:OR\s+REPLACE\s+)?(?:UNIQUE\s+)?(TABLE|INDEX|VIEW|PROCEDURE|PROC|FUNCTION|TRIGGER|SEQUENCE|TYPE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // DROP <object-type>
    // DROP DATABASE and DROP SCHEMA are intercepted by NeverAllowedDetector before this runs.
    private static readonly Regex DropObjectPattern = new(
        @"^\s*DROP\s+(TABLE|INDEX|VIEW|PROCEDURE|PROC|FUNCTION|TRIGGER|SEQUENCE|TYPE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ALTER TABLE <tablename> <sub-command>
    // The sub-command is the word immediately after the table name.
    private static readonly Regex AlterTableSubCommandPattern = new(
        @"^\s*ALTER\s+TABLE\s+\S+\s+(\w+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Quick presence check for ALTER TABLE
    private static readonly Regex IsAlterTable = new(
        @"^\s*ALTER\s+TABLE\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // RENAME TABLE (MySQL standalone)
    private static readonly Regex RenameTable = new(
        @"^\s*RENAME\s+TABLE\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Simple single-keyword DML / DQL commands
    private static readonly Regex SimpleCommandPattern = new(
        @"^\s*(INSERT|UPDATE|DELETE|SELECT|MERGE|REPLACE|TRUNCATE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Classifies a SQL statement and returns its canonical command name,
    /// or <c>null</c> if the statement is not a data-affecting command.
    /// </summary>
    public static string? Classify(string statement)
    {
        string stmt = StripComments(statement).Trim();
        if (stmt.Length == 0) return null;

        // ── CREATE ────────────────────────────────────────────────────────────
        Match m = CreatePattern.Match(stmt);
        if (m.Success)
        {
            string obj = Normalize(m.Groups[1].Value);
            return $"CREATE {obj}";
        }

        // ── DROP ──────────────────────────────────────────────────────────────
        m = DropObjectPattern.Match(stmt);
        if (m.Success)
        {
            string obj = Normalize(m.Groups[1].Value);
            return $"DROP {obj}";
        }

        // ── ALTER TABLE ───────────────────────────────────────────────────────
        if (IsAlterTable.IsMatch(stmt))
        {
            m = AlterTableSubCommandPattern.Match(stmt);
            if (!m.Success) return "ALTER TABLE";

            return m.Groups[1].Value.ToUpperInvariant() switch
            {
                "ADD"     => "ALTER TABLE ADD",
                "DROP"    => "ALTER TABLE DROP",
                "ALTER"   => "ALTER TABLE ALTER",     // MSSQL / Postgres: ALTER COLUMN
                "MODIFY"  => "ALTER TABLE MODIFY",    // MySQL / MariaDB
                "CHANGE"  => "ALTER TABLE CHANGE",    // MySQL / MariaDB
                "RENAME"  => "ALTER TABLE RENAME",
                "SET"     => "ALTER TABLE SET",        // Postgres SET DEFAULT / SET NOT NULL
                "ENABLE"  => "ALTER TABLE ENABLE",
                "DISABLE" => "ALTER TABLE DISABLE",
                _         => "ALTER TABLE"
            };
        }

        // ── RENAME TABLE (MySQL standalone) ──────────────────────────────────
        if (RenameTable.IsMatch(stmt)) return "RENAME TABLE";

        // ── Simple DML / DQL ─────────────────────────────────────────────────
        m = SimpleCommandPattern.Match(stmt);
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();

        // ── Not a recognised data command (PRINT, SET, DECLARE, GO, USE…) ───
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────

    // Normalise aliases: PROC → PROCEDURE
    private static string Normalize(string objectType) =>
        objectType.ToUpperInvariant() switch
        {
            "PROC" => "PROCEDURE",
            var v  => v
        };

    private static string StripComments(string sql)
    {
        string result = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        result = Regex.Replace(result, @"--[^\n]*", "");
        return result;
    }
}
