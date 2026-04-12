using Nova.Presets.Api.Configuration;
using Nova.Shared.Data;

namespace Nova.Presets.Api.Endpoints;

/// <summary>Shared helpers for Presets.Api endpoint DB access.</summary>
internal static class PresetsDbHelper
{
    internal static ISqlDialect Dialect(DbType dbType) => dbType switch
    {
        DbType.Postgres => new PostgresDialect(),
        DbType.MariaDb  => new MariaDbDialect(),
        _               => new MsSqlDialect()
    };

    internal static DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;

    /// <summary>
    /// Returns the dialect-correct branches query.
    /// MSSQL uses PascalCase legacy column names and ISNULL null-guard.
    /// Postgres/MariaDB use snake_case columns aliased to PascalCase so Dapper's
    /// constructor mapping can match them to <c>BranchRow</c>.
    /// </summary>
    internal static string BranchesQuery(PresetsDbSettings presetsDb)
    {
        ISqlDialect dialect  = Dialect(presetsDb.DbType);
        bool        isMsSql  = presetsDb.DbType == DbType.MsSql;

        if (isMsSql)
        {
            return """
                   SELECT br.BranchCode, br.BranchName, co.CompanyCode, co.CompanyName
                   FROM   presets.dbo.Branch  br
                   INNER  JOIN presets.dbo.Company co ON co.CompanyCode = br.CompanyCode
                   WHERE  co.TenantId         = @TenantId
                   AND    ISNULL(br.FrzInd,0) = 0
                   AND    ISNULL(co.FrzInd,0) = 0
                   ORDER  BY br.BranchName
                   """;
        }

        // Postgres / MariaDB — dialect handles table quoting via TableRef
        string branch  = dialect.TableRef("presets", "branch");
        string company = dialect.TableRef("presets", "company");
        string falsy   = dialect.BooleanLiteral(false);

        return $"""
                SELECT br.branch_code AS BranchCode, br.branch_name AS BranchName,
                       co.company_code AS CompanyCode, co.company_name AS CompanyName
                FROM   {branch}  br
                INNER  JOIN {company} co ON co.company_code = br.company_code
                WHERE  co.tenant_id = @TenantId
                AND    br.frz_ind   = {falsy}
                AND    co.frz_ind   = {falsy}
                ORDER  BY br.branch_name
                """;
    }

    /// <summary>
    /// Returns the dialect-correct query for all active status options scoped to the
    /// given tenant / company / branch, with most-specific-tier-wins resolution via
    /// ROW_NUMBER. Results are ordered by <c>serial_no</c>, then <c>label</c>.
    /// Parameters: <c>@TenantId</c>, <c>@CompanyCode</c>, <c>@BranchCode</c>.
    /// </summary>
    internal static string StatusOptionsQuerySql(PresetsDbSettings presetsDb)
    {
        ISqlDialect dialect = Dialect(presetsDb.DbType);
        string      table   = dialect.TableRef("presets", "user_status_options");
        string      falsy   = dialect.BooleanLiteral(false);

        return $"""
                WITH ranked AS (
                    SELECT status_code AS StatusCode,
                           label       AS Label,
                           colour      AS Colour,
                           serial_no,
                           ROW_NUMBER() OVER (
                               PARTITION BY status_code
                               ORDER BY
                                   CASE
                                       WHEN company_code <> 'XXXX' AND branch_code <> 'XXXX' THEN 3
                                       WHEN company_code <> 'XXXX'                            THEN 2
                                       ELSE                                                        1
                                   END DESC
                           ) AS rn
                    FROM   {table}
                    WHERE  tenant_id = @TenantId
                    AND    frz_ind   = {falsy}
                    AND    (
                               (company_code = 'XXXX'       AND branch_code = 'XXXX')
                            OR (company_code = @CompanyCode AND branch_code = 'XXXX')
                            OR (company_code = @CompanyCode AND branch_code = @BranchCode)
                           )
                )
                SELECT StatusCode, Label, Colour
                FROM   ranked
                WHERE  rn = 1
                ORDER  BY serial_no, Label
                """;
    }

    /// <summary>
    /// Returns the dialect-correct query to look up a single status option by
    /// <c>status_code</c>, applying the same tier-resolution logic as
    /// <see cref="StatusOptionsQuerySql"/>.
    /// Parameters: <c>@TenantId</c>, <c>@CompanyCode</c>, <c>@BranchCode</c>, <c>@StatusCode</c>.
    /// </summary>
    internal static string FindStatusOptionSql(PresetsDbSettings presetsDb)
    {
        ISqlDialect dialect = Dialect(presetsDb.DbType);
        string      table   = dialect.TableRef("presets", "user_status_options");
        string      falsy   = dialect.BooleanLiteral(false);

        return $"""
                WITH ranked AS (
                    SELECT status_code AS StatusCode,
                           label       AS Label,
                           colour      AS Colour,
                           ROW_NUMBER() OVER (
                               PARTITION BY status_code
                               ORDER BY
                                   CASE
                                       WHEN company_code <> 'XXXX' AND branch_code <> 'XXXX' THEN 3
                                       WHEN company_code <> 'XXXX'                            THEN 2
                                       ELSE                                                        1
                                   END DESC
                           ) AS rn
                    FROM   {table}
                    WHERE  tenant_id   = @TenantId
                    AND    status_code = @StatusCode
                    AND    frz_ind     = {falsy}
                    AND    (
                               (company_code = 'XXXX'       AND branch_code = 'XXXX')
                            OR (company_code = @CompanyCode AND branch_code = 'XXXX')
                            OR (company_code = @CompanyCode AND branch_code = @BranchCode)
                           )
                )
                SELECT StatusCode, Label, Colour
                FROM   ranked
                WHERE  rn = 1
                """;
    }

    /// <summary>
    /// Returns the dialect-correct UPSERT SQL for the default-password reset against
    /// <c>nova_auth.tenant_user_auth</c>. Sets <c>password_hash</c>,
    /// <c>must_change_password = true</c>, clears <c>failed_login_count</c> and
    /// <c>locked_until</c>. Inserts the row if it does not yet exist.
    /// Parameters: <c>@TenantId</c>, <c>@TargetUserId</c>, <c>@PasswordHash</c>,
    /// <c>@UpdatedBy</c>, <c>@Now</c>.
    /// </summary>
    internal static string DefaultPasswordUpsertSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      table   = dialect.TableRef("nova_auth", "tenant_user_auth");
        string      truthy  = dialect.BooleanLiteral(true);
        string      falsy   = dialect.BooleanLiteral(false);

        return dbType switch
        {
            DbType.MsSql => $"""
                             MERGE INTO {table} WITH (HOLDLOCK) AS target
                             USING (SELECT @TenantId AS tenant_id, @TargetUserId AS user_id) AS source
                                   ON target.tenant_id = source.tenant_id
                                  AND target.user_id   = source.user_id
                             WHEN MATCHED THEN
                                 UPDATE SET password_hash        = @PasswordHash,
                                            must_change_password = 1,
                                            failed_login_count   = 0,
                                            locked_until         = NULL,
                                            updated_on           = @Now,
                                            updated_by           = @UpdatedBy,
                                            updated_at           = 'Nova.Presets.Api'
                             WHEN NOT MATCHED THEN
                                 INSERT (tenant_id, user_id, password_hash, must_change_password,
                                         totp_enabled, failed_login_count,
                                         frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
                                 VALUES (@TenantId, @TargetUserId, @PasswordHash, 1,
                                         0, 0,
                                         0, @UpdatedBy, @Now, @UpdatedBy, @Now, 'Nova.Presets.Api');
                             """,

            DbType.Postgres => $"""
                                INSERT INTO {table}
                                    (tenant_id, user_id, password_hash, must_change_password,
                                     totp_enabled, failed_login_count,
                                     frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
                                VALUES
                                    (@TenantId, @TargetUserId, @PasswordHash, {truthy},
                                     {falsy}, 0,
                                     {falsy}, @UpdatedBy, @Now, @UpdatedBy, @Now, 'Nova.Presets.Api')
                                ON CONFLICT (tenant_id, user_id) DO UPDATE SET
                                    password_hash        = EXCLUDED.password_hash,
                                    must_change_password = {truthy},
                                    failed_login_count   = 0,
                                    locked_until         = NULL,
                                    updated_on           = EXCLUDED.updated_on,
                                    updated_by           = EXCLUDED.updated_by,
                                    updated_at           = EXCLUDED.updated_at
                                """,

            _ /* MariaDB */ => $"""
                                INSERT INTO {table}
                                    (`tenant_id`, `user_id`, `password_hash`, `must_change_password`,
                                     `totp_enabled`, `failed_login_count`,
                                     `frz_ind`, `created_by`, `created_on`, `updated_by`, `updated_on`, `updated_at`)
                                VALUES
                                    (@TenantId, @TargetUserId, @PasswordHash, 1,
                                     0, 0,
                                     0, @UpdatedBy, @Now, @UpdatedBy, @Now, 'Nova.Presets.Api')
                                ON DUPLICATE KEY UPDATE
                                    `password_hash`        = VALUES(`password_hash`),
                                    `must_change_password` = 1,
                                    `failed_login_count`   = 0,
                                    `locked_until`         = NULL,
                                    `updated_on`           = VALUES(`updated_on`),
                                    `updated_by`           = VALUES(`updated_by`),
                                    `updated_at`           = VALUES(`updated_at`)
                                """
        };
    }

    /// <summary>
    /// Returns the dialect-correct UPSERT SQL for <c>tenant_user_status</c>.
    /// </summary>
    internal static string StatusUpsertSql(PresetsDbSettings presetsDb)
    {
        ISqlDialect dialect = Dialect(presetsDb.DbType);
        string      table   = dialect.TableRef("presets", "tenant_user_status");

        return presetsDb.DbType switch
        {
            DbType.MsSql => $"""
                             MERGE INTO {table} WITH (HOLDLOCK) AS target
                             USING (SELECT @TenantId AS tenant_id, @UserId AS user_id) AS source
                                   ON target.tenant_id = source.tenant_id AND target.user_id = source.user_id
                             WHEN MATCHED THEN
                                 UPDATE SET status_id    = @StatusId,
                                            status_label = @StatusLabel,
                                            status_note  = @StatusNote,
                                            updated_on   = @Now,
                                            updated_by   = 'system',
                                            updated_at   = 'Nova.Presets.Api'
                             WHEN NOT MATCHED THEN
                                 INSERT (tenant_id, user_id, status_id, status_label, status_note,
                                         frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
                                 VALUES (@TenantId, @UserId, @StatusId, @StatusLabel, @StatusNote,
                                         0, 'system', @Now, 'system', @Now, 'Nova.Presets.Api');
                             """,

            DbType.Postgres => $"""
                                INSERT INTO {table}
                                    (tenant_id, user_id, status_id, status_label, status_note,
                                     frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
                                VALUES
                                    (@TenantId, @UserId, @StatusId, @StatusLabel, @StatusNote,
                                     false, 'system', @Now, 'system', @Now, 'Nova.Presets.Api')
                                ON CONFLICT (tenant_id, user_id) DO UPDATE SET
                                    status_id    = EXCLUDED.status_id,
                                    status_label = EXCLUDED.status_label,
                                    status_note  = EXCLUDED.status_note,
                                    updated_on   = EXCLUDED.updated_on,
                                    updated_by   = EXCLUDED.updated_by,
                                    updated_at   = EXCLUDED.updated_at
                                """,

            _ /* MariaDB */ => $"""
                                INSERT INTO {table}
                                    (`tenant_id`, `user_id`, `status_id`, `status_label`, `status_note`,
                                     `frz_ind`, `created_by`, `created_on`, `updated_by`, `updated_on`, `updated_at`)
                                VALUES
                                    (@TenantId, @UserId, @StatusId, @StatusLabel, @StatusNote,
                                     0, 'system', @Now, 'system', @Now, 'Nova.Presets.Api')
                                ON DUPLICATE KEY UPDATE
                                    `status_id`    = VALUES(`status_id`),
                                    `status_label` = VALUES(`status_label`),
                                    `status_note`  = VALUES(`status_note`),
                                    `updated_on`   = VALUES(`updated_on`),
                                    `updated_by`   = VALUES(`updated_by`),
                                    `updated_at`   = VALUES(`updated_at`)
                                """
        };
    }
}
