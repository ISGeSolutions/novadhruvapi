using Nova.OpsGroups.Api.Configuration;
using Nova.Shared.Data;

namespace Nova.OpsGroups.Api.Endpoints;

internal static class OpsGroupsDbHelper
{
    internal static ISqlDialect Dialect(DbType dbType) => dbType switch
    {
        DbType.Postgres => new PostgresDialect(),
        DbType.MariaDb  => new MariaDbDialect(),
        _               => new MsSqlDialect()
    };

    internal static DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;

    internal static string TeamMembersQuerySql(AuthDbSettings authDb)
    {
        ISqlDialect dialect = Dialect(authDb.DbType);
        string      rights  = dialect.TableRef("nova_auth", "user_security_rights");
        string      profile = dialect.TableRef("nova_auth", "tenant_user_profile");
        string      falsy   = dialect.BooleanLiteral(false);

        return $"""
                SELECT  r.user_id,
                        p.display_name,
                        r.role_code,
                        r.role_flags
                FROM    {rights}  r
                JOIN    {profile} p ON p.tenant_id = r.tenant_id
                                   AND p.user_id   = r.user_id
                WHERE   r.tenant_id    = @TenantId
                AND     (r.company_code = @CompanyCode OR r.company_code = 'XXXX')
                AND     (r.branch_code  = @BranchCode  OR r.branch_code  = 'XXXX')
                AND     r.role_code    IN ('OPSMGR', 'OPSEXEC')
                AND     r.frz_ind      = {falsy}
                AND     p.frz_ind      = {falsy}
                ORDER BY p.display_name, r.role_code
                """;
    }

    // -------------------------------------------------------------------------
    // Departures
    // -------------------------------------------------------------------------

    internal static string DeparturesListSql(OpsGroupsDbSettings db, DepartureFilters f, int skip, int take)
    {
        ISqlDialect dialect  = Dialect(db.DbType);
        string      table    = dialect.TableRef("opsgroups", "grouptour_departures");
        string      falsy    = dialect.BooleanLiteral(false);
        string      paging   = dialect.OffsetFetchClause(skip, take);

        var where = new List<string>
        {
            "d.tenant_id = @TenantId",
            $"d.frz_ind  = {falsy}",
        };

        if (f.DateFrom.HasValue)     where.Add("d.departure_date >= @DateFrom");
        if (f.DateTo.HasValue)       where.Add("d.departure_date <= @DateTo");
        if (!string.IsNullOrEmpty(f.BranchCode))        where.Add("d.branch_code = @BranchCode");
        if (!string.IsNullOrEmpty(f.SeriesCode))        where.Add("d.series_code = @SeriesCode");
        if (!string.IsNullOrEmpty(f.DestinationCode))   where.Add("d.destination_code = @DestinationCode");
        if (!string.IsNullOrEmpty(f.OpsManager))        where.Add("d.ops_manager_initials = @OpsManager");
        if (!string.IsNullOrEmpty(f.OpsExec))           where.Add("d.ops_exec_initials = @OpsExec");
        if (!string.IsNullOrEmpty(f.Search))            where.Add("(d.series_name LIKE @Search OR d.destination_name LIKE @Search OR d.departure_id LIKE @Search)");

        string whereClause = string.Join(" AND ", where);

        return $"""
                SELECT  d.id,
                        d.tenant_id,
                        d.departure_id,
                        d.series_code,
                        d.series_name,
                        d.departure_date,
                        d.return_date,
                        d.destination_code,
                        d.destination_name,
                        d.branch_code,
                        d.ops_manager_initials,
                        d.ops_manager_name,
                        d.ops_exec_initials,
                        d.ops_exec_name,
                        d.pax_count,
                        d.booking_count,
                        d.gtd,
                        d.notes,
                        d.updated_on
                FROM    {table} d
                WHERE   {whereClause}
                ORDER BY d.departure_date, d.departure_id
                {paging}
                """;
    }

    internal static string DeparturesCountSql(OpsGroupsDbSettings db, DepartureFilters f)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_departures");
        string      falsy   = dialect.BooleanLiteral(false);

        var where = new List<string>
        {
            "d.tenant_id = @TenantId",
            $"d.frz_ind  = {falsy}",
        };

        if (f.DateFrom.HasValue)     where.Add("d.departure_date >= @DateFrom");
        if (f.DateTo.HasValue)       where.Add("d.departure_date <= @DateTo");
        if (!string.IsNullOrEmpty(f.BranchCode))        where.Add("d.branch_code = @BranchCode");
        if (!string.IsNullOrEmpty(f.SeriesCode))        where.Add("d.series_code = @SeriesCode");
        if (!string.IsNullOrEmpty(f.DestinationCode))   where.Add("d.destination_code = @DestinationCode");
        if (!string.IsNullOrEmpty(f.OpsManager))        where.Add("d.ops_manager_initials = @OpsManager");
        if (!string.IsNullOrEmpty(f.OpsExec))           where.Add("d.ops_exec_initials = @OpsExec");
        if (!string.IsNullOrEmpty(f.Search))            where.Add("(d.series_name LIKE @Search OR d.destination_name LIKE @Search OR d.departure_id LIKE @Search)");

        string whereClause = string.Join(" AND ", where);

        return $"""
                SELECT COUNT(*) FROM {table} d WHERE {whereClause}
                """;
    }

    internal static string DepartureByIdSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_departures");
        string      falsy   = dialect.BooleanLiteral(false);

        return $"""
                SELECT  d.id,
                        d.tenant_id,
                        d.departure_id,
                        d.series_code,
                        d.series_name,
                        d.departure_date,
                        d.return_date,
                        d.destination_code,
                        d.destination_name,
                        d.branch_code,
                        d.ops_manager_initials,
                        d.ops_manager_name,
                        d.ops_exec_initials,
                        d.ops_exec_name,
                        d.pax_count,
                        d.booking_count,
                        d.gtd,
                        d.notes,
                        d.updated_on
                FROM    {table} d
                WHERE   d.tenant_id    = @TenantId
                AND     d.departure_id = @DepartureId
                AND     d.frz_ind      = {falsy}
                """;
    }

    internal static string GroupTasksByDepartureIdsSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_departure_group_tasks");
        string      falsy   = dialect.BooleanLiteral(false);

        return $"""
                SELECT  t.departure_id,
                        t.group_task_id,
                        t.template_code,
                        t.status,
                        t.due_date,
                        t.completed_date,
                        t.notes,
                        t.source,
                        t.required,
                        t.updated_on
                FROM    {table} t
                WHERE   t.tenant_id    = @TenantId
                AND     t.departure_id IN @DepartureIds
                AND     t.frz_ind      = {falsy}
                ORDER BY t.template_code
                """;
    }

    // -------------------------------------------------------------------------
    // Group tasks — single update
    // -------------------------------------------------------------------------

    internal static string GroupTaskUpdateSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_departure_group_tasks");

        return $"""
                UPDATE {table}
                SET    status         = @Status,
                       notes          = @Notes,
                       completed_date = @CompletedDate,
                       updated_on     = @Now,
                       updated_by     = @UpdatedBy,
                       updated_at     = 'Nova.OpsGroups.Api'
                WHERE  tenant_id      = @TenantId
                AND    departure_id   = @DepartureId
                AND    group_task_id  = @GroupTaskId
                """;
    }

    internal static string GroupTaskByIdSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_departure_group_tasks");
        string      falsy   = dialect.BooleanLiteral(false);

        return $"""
                SELECT  departure_id,
                        group_task_id,
                        template_code,
                        status,
                        due_date,
                        completed_date,
                        notes,
                        source,
                        updated_on
                FROM    {table}
                WHERE   tenant_id     = @TenantId
                AND     departure_id  = @DepartureId
                AND     group_task_id = @GroupTaskId
                AND     frz_ind       = {falsy}
                """;
    }

    internal static string GroupTaskCurrentStatusSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_departure_group_tasks");
        string      falsy   = dialect.BooleanLiteral(false);

        return $"""
                SELECT  departure_id,
                        group_task_id,
                        status
                FROM    {table}
                WHERE   tenant_id     = @TenantId
                AND     departure_id  = @DepartureId
                AND     group_task_id = @GroupTaskId
                AND     frz_ind       = {falsy}
                """;
    }

    // -------------------------------------------------------------------------
    // SLA rules
    // -------------------------------------------------------------------------

    internal static string SlaRulesListSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_sla_rules");

        return $"""
                SELECT  id,
                        level,
                        scope_key,
                        tour_code,
                        group_task_code,
                        reference_date,
                        group_task_sla_offset_days,
                        version,
                        updated_on
                FROM    {table}
                WHERE   tenant_id = @TenantId
                ORDER BY level, group_task_code, reference_date
                """;
    }

    internal static string SlaRuleUpsertSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_sla_rules");

        return db.DbType switch
        {
            DbType.MsSql => $"""
                             MERGE INTO {table} WITH (HOLDLOCK) AS target
                             USING (SELECT @TenantId AS tenant_id, @ScopeKey AS scope_key,
                                          @GroupTaskCode AS group_task_code, @ReferenceDate AS reference_date) AS source
                                   ON target.tenant_id       = source.tenant_id
                                  AND target.scope_key       = source.scope_key
                                  AND target.group_task_code = source.group_task_code
                                  AND target.reference_date  = source.reference_date
                             WHEN MATCHED THEN
                                 UPDATE SET level                       = @Level,
                                            tour_code                   = @TourCode,
                                            group_task_sla_offset_days  = @OffsetDays,
                                            version                     = @Version,
                                            updated_on                  = @Now,
                                            updated_by                  = @UpdatedBy,
                                            updated_at                  = 'Nova.OpsGroups.Api'
                             WHEN NOT MATCHED THEN
                                 INSERT (id, tenant_id, level, scope_key, tour_code, group_task_code,
                                         reference_date, group_task_sla_offset_days, version,
                                         created_by, created_on, updated_by, updated_on, updated_at)
                                 VALUES (@Id, @TenantId, @Level, @ScopeKey, @TourCode, @GroupTaskCode,
                                         @ReferenceDate, @OffsetDays, @Version,
                                         @UpdatedBy, @Now, @UpdatedBy, @Now, 'Nova.OpsGroups.Api');
                             """,

            DbType.Postgres => $"""
                                INSERT INTO {table}
                                    (id, tenant_id, level, scope_key, tour_code, group_task_code,
                                     reference_date, group_task_sla_offset_days, version,
                                     created_by, created_on, updated_by, updated_on, updated_at)
                                VALUES
                                    (@Id, @TenantId, @Level, @ScopeKey, @TourCode, @GroupTaskCode,
                                     @ReferenceDate, @OffsetDays, @Version,
                                     @UpdatedBy, @Now, @UpdatedBy, @Now, 'Nova.OpsGroups.Api')
                                ON CONFLICT (tenant_id, scope_key, group_task_code, reference_date) DO UPDATE SET
                                    level                      = EXCLUDED.level,
                                    tour_code                  = EXCLUDED.tour_code,
                                    group_task_sla_offset_days = EXCLUDED.group_task_sla_offset_days,
                                    version                    = EXCLUDED.version,
                                    updated_on                 = EXCLUDED.updated_on,
                                    updated_by                 = EXCLUDED.updated_by,
                                    updated_at                 = EXCLUDED.updated_at
                                """,

            _ /* MariaDB */ => $"""
                                INSERT INTO {table}
                                    (`id`, `tenant_id`, `level`, `scope_key`, `tour_code`, `group_task_code`,
                                     `reference_date`, `group_task_sla_offset_days`, `version`,
                                     `created_by`, `created_on`, `updated_by`, `updated_on`, `updated_at`)
                                VALUES
                                    (@Id, @TenantId, @Level, @ScopeKey, @TourCode, @GroupTaskCode,
                                     @ReferenceDate, @OffsetDays, @Version,
                                     @UpdatedBy, @Now, @UpdatedBy, @Now, 'Nova.OpsGroups.Api')
                                ON DUPLICATE KEY UPDATE
                                    `level`                      = VALUES(`level`),
                                    `tour_code`                  = VALUES(`tour_code`),
                                    `group_task_sla_offset_days` = VALUES(`group_task_sla_offset_days`),
                                    `version`                    = VALUES(`version`),
                                    `updated_on`                 = VALUES(`updated_on`),
                                    `updated_by`                 = VALUES(`updated_by`),
                                    `updated_at`                 = VALUES(`updated_at`)
                                """
        };
    }

    // -------------------------------------------------------------------------
    // SLA hierarchy
    // -------------------------------------------------------------------------

    internal static string SlaRulesForTenantSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_sla_rules");

        return $"""
                SELECT  id,
                        level,
                        scope_key,
                        tour_code,
                        group_task_code,
                        reference_date,
                        group_task_sla_offset_days,
                        version,
                        updated_on
                FROM    {table}
                WHERE   tenant_id = @TenantId
                ORDER BY level, scope_key, group_task_code, reference_date
                """;
    }

    internal static string SlaRuleDeleteSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_sla_rules");

        return $"""
                DELETE FROM {table}
                WHERE   tenant_id       = @TenantId
                AND     scope_key       = @ScopeKey
                AND     group_task_code = @GroupTaskCode
                AND     reference_date  = @ReferenceDate
                """;
    }

    internal static string SlaAuditInsertSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_sla_rule_audit");

        return $"""
                INSERT INTO {table}
                    (id, tenant_id, scope_key, scope_label, group_task_code, reference_date,
                     old_value, new_value, changed_by_name, changed_at)
                VALUES
                    (@Id, @TenantId, @ScopeKey, @ScopeLabel, @GroupTaskCode, @ReferenceDate,
                     @OldValue, @NewValue, @ChangedByName, @ChangedAt)
                """;
    }

    internal static string SlaAuditQuerySql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_sla_rule_audit");

        return $"""
                SELECT  id,
                        scope_key,
                        scope_label,
                        group_task_code,
                        reference_date,
                        old_value,
                        new_value,
                        changed_by_name,
                        changed_at
                FROM    {table}
                WHERE   tenant_id = @TenantId
                AND     scope_key = @ScopeKey
                ORDER BY changed_at DESC
                """;
    }

    internal static string SlaAuditCountSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_sla_rule_audit");

        return $"""
                SELECT COUNT(*) FROM {table}
                WHERE  tenant_id = @TenantId
                AND    scope_key = @ScopeKey
                """;
    }

    // -------------------------------------------------------------------------
    // Business rules
    // -------------------------------------------------------------------------

    internal static string BusinessRulesFetchSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_task_business_rules");

        return $"""
                SELECT  tenant_id,
                        company_code,
                        branch_code,
                        overdue_critical_days,
                        overdue_warning_days,
                        readiness_method,
                        risk_red_threshold,
                        risk_amber_threshold,
                        risk_green_threshold,
                        heatmap_red_max,
                        heatmap_amber_max,
                        auto_mark_overdue,
                        include_na_in_readiness,
                        updated_at,
                        updated_by
                FROM    {table}
                WHERE   tenant_id    = @TenantId
                AND     company_code = @CompanyCode
                AND     branch_code  = @BranchCode
                """;
    }

    internal static string BusinessRulesUpsertSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_task_business_rules");

        return db.DbType switch
        {
            DbType.MsSql => $"""
                             MERGE INTO {table} WITH (HOLDLOCK) AS target
                             USING (SELECT @TenantId AS tenant_id, @CompanyCode AS company_code, @BranchCode AS branch_code) AS source
                                   ON target.tenant_id    = source.tenant_id
                                  AND target.company_code = source.company_code
                                  AND target.branch_code  = source.branch_code
                             WHEN MATCHED THEN
                                 UPDATE SET overdue_critical_days   = @OverdueCriticalDays,
                                            overdue_warning_days    = @OverdueWarningDays,
                                            readiness_method        = @ReadinessMethod,
                                            risk_red_threshold      = @RiskRedThreshold,
                                            risk_amber_threshold    = @RiskAmberThreshold,
                                            risk_green_threshold    = @RiskGreenThreshold,
                                            heatmap_red_max         = @HeatmapRedMax,
                                            heatmap_amber_max       = @HeatmapAmberMax,
                                            auto_mark_overdue       = @AutoMarkOverdue,
                                            include_na_in_readiness = @IncludeNaInReadiness,
                                            updated_at              = @Now,
                                            updated_by              = @UpdatedBy
                             WHEN NOT MATCHED THEN
                                 INSERT (tenant_id, company_code, branch_code,
                                         overdue_critical_days, overdue_warning_days, readiness_method,
                                         risk_red_threshold, risk_amber_threshold, risk_green_threshold,
                                         heatmap_red_max, heatmap_amber_max, auto_mark_overdue,
                                         include_na_in_readiness, updated_at, updated_by)
                                 VALUES (@TenantId, @CompanyCode, @BranchCode,
                                         @OverdueCriticalDays, @OverdueWarningDays, @ReadinessMethod,
                                         @RiskRedThreshold, @RiskAmberThreshold, @RiskGreenThreshold,
                                         @HeatmapRedMax, @HeatmapAmberMax, @AutoMarkOverdue,
                                         @IncludeNaInReadiness, @Now, @UpdatedBy);
                             """,

            DbType.Postgres => $"""
                                INSERT INTO {table}
                                    (tenant_id, company_code, branch_code,
                                     overdue_critical_days, overdue_warning_days, readiness_method,
                                     risk_red_threshold, risk_amber_threshold, risk_green_threshold,
                                     heatmap_red_max, heatmap_amber_max, auto_mark_overdue,
                                     include_na_in_readiness, updated_at, updated_by)
                                VALUES
                                    (@TenantId, @CompanyCode, @BranchCode,
                                     @OverdueCriticalDays, @OverdueWarningDays, @ReadinessMethod,
                                     @RiskRedThreshold, @RiskAmberThreshold, @RiskGreenThreshold,
                                     @HeatmapRedMax, @HeatmapAmberMax, @AutoMarkOverdue,
                                     @IncludeNaInReadiness, @Now, @UpdatedBy)
                                ON CONFLICT (tenant_id, company_code, branch_code) DO UPDATE SET
                                    overdue_critical_days   = EXCLUDED.overdue_critical_days,
                                    overdue_warning_days    = EXCLUDED.overdue_warning_days,
                                    readiness_method        = EXCLUDED.readiness_method,
                                    risk_red_threshold      = EXCLUDED.risk_red_threshold,
                                    risk_amber_threshold    = EXCLUDED.risk_amber_threshold,
                                    risk_green_threshold    = EXCLUDED.risk_green_threshold,
                                    heatmap_red_max         = EXCLUDED.heatmap_red_max,
                                    heatmap_amber_max       = EXCLUDED.heatmap_amber_max,
                                    auto_mark_overdue       = EXCLUDED.auto_mark_overdue,
                                    include_na_in_readiness = EXCLUDED.include_na_in_readiness,
                                    updated_at              = EXCLUDED.updated_at,
                                    updated_by              = EXCLUDED.updated_by
                                """,

            _ /* MariaDB */ => $"""
                                INSERT INTO {table}
                                    (`tenant_id`, `company_code`, `branch_code`,
                                     `overdue_critical_days`, `overdue_warning_days`, `readiness_method`,
                                     `risk_red_threshold`, `risk_amber_threshold`, `risk_green_threshold`,
                                     `heatmap_red_max`, `heatmap_amber_max`, `auto_mark_overdue`,
                                     `include_na_in_readiness`, `updated_at`, `updated_by`)
                                VALUES
                                    (@TenantId, @CompanyCode, @BranchCode,
                                     @OverdueCriticalDays, @OverdueWarningDays, @ReadinessMethod,
                                     @RiskRedThreshold, @RiskAmberThreshold, @RiskGreenThreshold,
                                     @HeatmapRedMax, @HeatmapAmberMax, @AutoMarkOverdue,
                                     @IncludeNaInReadiness, @Now, @UpdatedBy)
                                ON DUPLICATE KEY UPDATE
                                    `overdue_critical_days`   = VALUES(`overdue_critical_days`),
                                    `overdue_warning_days`    = VALUES(`overdue_warning_days`),
                                    `readiness_method`        = VALUES(`readiness_method`),
                                    `risk_red_threshold`      = VALUES(`risk_red_threshold`),
                                    `risk_amber_threshold`    = VALUES(`risk_amber_threshold`),
                                    `risk_green_threshold`    = VALUES(`risk_green_threshold`),
                                    `heatmap_red_max`         = VALUES(`heatmap_red_max`),
                                    `heatmap_amber_max`       = VALUES(`heatmap_amber_max`),
                                    `auto_mark_overdue`       = VALUES(`auto_mark_overdue`),
                                    `include_na_in_readiness` = VALUES(`include_na_in_readiness`),
                                    `updated_at`              = VALUES(`updated_at`),
                                    `updated_by`              = VALUES(`updated_by`)
                                """
        };
    }

    // -------------------------------------------------------------------------
    // Summary / dashboard
    // -------------------------------------------------------------------------

    internal static string SummaryStatsSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect  = Dialect(db.DbType);
        string      depTable = dialect.TableRef("opsgroups", "grouptour_departures");
        string      taskTable = dialect.TableRef("opsgroups", "grouptour_departure_group_tasks");
        string      falsy    = dialect.BooleanLiteral(false);

        return $"""
                SELECT
                    (SELECT COUNT(*) FROM {depTable} d
                     WHERE d.tenant_id = @TenantId AND d.frz_ind = {falsy}
                     AND d.departure_date >= @DateFrom AND d.departure_date <= @DateTo) AS TotalDepartures,
                    (SELECT COUNT(*) FROM {taskTable} t
                     WHERE t.tenant_id = @TenantId AND t.frz_ind = {falsy}
                     AND t.status = 'overdue'
                     AND t.departure_id IN (SELECT d2.departure_id FROM {depTable} d2
                         WHERE d2.tenant_id = @TenantId AND d2.frz_ind = {falsy}
                         AND d2.departure_date >= @DateFrom AND d2.departure_date <= @DateTo)) AS OverdueGroupTasks,
                    (SELECT COUNT(*) FROM {taskTable} t
                     WHERE t.tenant_id = @TenantId AND t.frz_ind = {falsy}
                     AND t.due_date >= @Today AND t.due_date <= @WeekEnd
                     AND t.status NOT IN ('complete', 'not_applicable')
                     AND t.departure_id IN (SELECT d2.departure_id FROM {depTable} d2
                         WHERE d2.tenant_id = @TenantId AND d2.frz_ind = {falsy}
                         AND d2.departure_date >= @DateFrom AND d2.departure_date <= @DateTo)) AS DueThisWeek,
                    (SELECT COUNT(*) FROM {taskTable} t
                     WHERE t.tenant_id = @TenantId AND t.frz_ind = {falsy}
                     AND t.status = 'complete'
                     AND t.updated_on >= @TodayUtc
                     AND t.departure_id IN (SELECT d2.departure_id FROM {depTable} d2
                         WHERE d2.tenant_id = @TenantId AND d2.frz_ind = {falsy}
                         AND d2.departure_date >= @DateFrom AND d2.departure_date <= @DateTo)) AS CompletedToday
                """;
    }

    internal static string FacetsSql(OpsGroupsDbSettings db, DepartureFilters f)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_departures");
        string      falsy   = dialect.BooleanLiteral(false);

        var where = new List<string>
        {
            "d.tenant_id = @TenantId",
            $"d.frz_ind  = {falsy}",
        };

        if (f.DateFrom.HasValue)     where.Add("d.departure_date >= @DateFrom");
        if (f.DateTo.HasValue)       where.Add("d.departure_date <= @DateTo");
        if (!string.IsNullOrEmpty(f.BranchCode))        where.Add("d.branch_code = @BranchCode");
        if (!string.IsNullOrEmpty(f.SeriesCode))        where.Add("d.series_code = @SeriesCode");
        if (!string.IsNullOrEmpty(f.DestinationCode))   where.Add("d.destination_code = @DestinationCode");
        if (!string.IsNullOrEmpty(f.OpsManager))        where.Add("d.ops_manager_initials = @OpsManager");
        if (!string.IsNullOrEmpty(f.OpsExec))           where.Add("d.ops_exec_initials = @OpsExec");
        if (!string.IsNullOrEmpty(f.Search))            where.Add("(d.series_name LIKE @Search OR d.destination_name LIKE @Search OR d.departure_id LIKE @Search)");

        string whereClause = string.Join(" AND ", where);

        return $"""
                SELECT DISTINCT
                    d.branch_code,
                    d.ops_manager_initials,
                    d.ops_manager_name,
                    d.ops_exec_initials,
                    d.ops_exec_name,
                    d.series_code,
                    d.series_name
                FROM {table} d
                WHERE {whereClause}
                ORDER BY d.branch_code, d.series_code
                """;
    }

    internal static string SeriesAggregateSql(OpsGroupsDbSettings db, DepartureFilters f)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_departures");
        string      falsy   = dialect.BooleanLiteral(false);

        var where = new List<string>
        {
            "d.tenant_id = @TenantId",
            $"d.frz_ind  = {falsy}",
        };

        if (f.DateFrom.HasValue)     where.Add("d.departure_date >= @DateFrom");
        if (f.DateTo.HasValue)       where.Add("d.departure_date <= @DateTo");
        if (!string.IsNullOrEmpty(f.BranchCode))        where.Add("d.branch_code = @BranchCode");
        if (!string.IsNullOrEmpty(f.SeriesCode))        where.Add("d.series_code = @SeriesCode");
        if (!string.IsNullOrEmpty(f.DestinationCode))   where.Add("d.destination_code = @DestinationCode");
        if (!string.IsNullOrEmpty(f.OpsManager))        where.Add("d.ops_manager_initials = @OpsManager");
        if (!string.IsNullOrEmpty(f.OpsExec))           where.Add("d.ops_exec_initials = @OpsExec");
        if (!string.IsNullOrEmpty(f.Search))            where.Add("(d.series_name LIKE @Search OR d.destination_name LIKE @Search OR d.departure_id LIKE @Search)");

        string whereClause = string.Join(" AND ", where);

        return $"""
                SELECT  d.series_code,
                        d.series_name,
                        SUM(d.pax_count)     AS TotalPax,
                        COUNT(d.departure_id) AS TotalDepartures
                FROM    {table} d
                WHERE   {whereClause}
                GROUP BY d.series_code, d.series_name
                ORDER BY d.series_code
                """;
    }

    internal static string HeatmapDatesSql(OpsGroupsDbSettings db, DepartureFilters f)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("opsgroups", "grouptour_departures");
        string      falsy   = dialect.BooleanLiteral(false);
        string      paging  = dialect.OffsetFetchClause(0, 28);

        var where = new List<string>
        {
            "d.tenant_id = @TenantId",
            $"d.frz_ind  = {falsy}",
        };

        if (f.DateFrom.HasValue)     where.Add("d.departure_date >= @DateFrom");
        if (f.DateTo.HasValue)       where.Add("d.departure_date <= @DateTo");
        if (!string.IsNullOrEmpty(f.BranchCode))        where.Add("d.branch_code = @BranchCode");
        if (!string.IsNullOrEmpty(f.SeriesCode))        where.Add("d.series_code = @SeriesCode");
        if (!string.IsNullOrEmpty(f.DestinationCode))   where.Add("d.destination_code = @DestinationCode");
        if (!string.IsNullOrEmpty(f.OpsManager))        where.Add("d.ops_manager_initials = @OpsManager");
        if (!string.IsNullOrEmpty(f.OpsExec))           where.Add("d.ops_exec_initials = @OpsExec");

        if (f.WindowStart.HasValue) where.Add("d.departure_date >= @WindowStart");

        string whereClause = string.Join(" AND ", where);

        return $"""
                SELECT DISTINCT d.departure_date
                FROM    {table} d
                WHERE   {whereClause}
                ORDER BY d.departure_date
                {paging}
                """;
    }

    // -------------------------------------------------------------------------
    // Presets DB — task codes available
    // -------------------------------------------------------------------------

    internal static string TaskTemplatesForTenantSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      table   = dialect.TableRef("presets", "group_task_templates");
        string      falsy   = dialect.BooleanLiteral(false);

        return $"""
                SELECT  code,
                        name,
                        source
                FROM    {table}
                WHERE   tenant_id = @TenantId
                AND     frz_ind   = {falsy}
                ORDER BY code
                """;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // TODO: fill in logic for each branch using the business rules values.
    // readinessMethod: "required_only" → Completed Required / Total Required
    //                  "all_tasks"     → Completed All / Total All (excl. N/A unless includeNaInReadiness)
    // includeNaInReadiness: when true, count "not_applicable" tasks as complete in both modes.
    internal static int ComputeReadinessPct(
        IEnumerable<GroupTaskRow> tasks,
        string readinessMethod      = "required_only",
        bool   includeNaInReadiness = false)
    {
        var list = tasks.ToList();
        if (list.Count == 0) return 0;

        // TODO: implement required_only branch
        // var required = list.Where(t => t.Required).ToList();
        // if (required.Count == 0) return 0;
        // int done = required.Count(t => t.Status == "complete" || (includeNaInReadiness && t.Status == "not_applicable"));
        // return (int)Math.Round(done / (double)required.Count * 100, MidpointRounding.AwayFromZero);

        // TODO: implement all_tasks branch
        // var eligible = list.Where(t => includeNaInReadiness || t.Status != "not_applicable").ToList();
        // if (eligible.Count == 0) return 0;
        // int done = eligible.Count(t => t.Status == "complete" || (includeNaInReadiness && t.Status == "not_applicable"));
        // return (int)Math.Round(done / (double)eligible.Count * 100, MidpointRounding.AwayFromZero);

        // Placeholder — remove once branches above are uncommented and correct.
        int doneFallback = list.Count(t => t.Status is "complete" or "not_applicable");
        return (int)Math.Round(doneFallback / (double)list.Count * 100, MidpointRounding.AwayFromZero);
    }

    internal static string ComputeRiskLevel(int readinessPct)
    {
        if (readinessPct < 40) return "red";
        if (readinessPct < 80) return "amber";
        return "green";
    }

    internal static string ComputeVersionToken(DateTimeOffset? updatedOn)
    {
        if (!updatedOn.HasValue) return "v0-none";
        byte[] bytes  = BitConverter.GetBytes(updatedOn.Value.ToUnixTimeMilliseconds());
        string token  = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"v0-{token[..Math.Min(8, token.Length)]}";
    }
}

// -------------------------------------------------------------------------
// Shared filter record
// -------------------------------------------------------------------------
internal sealed record DepartureFilters(
    DateOnly?  DateFrom,
    DateOnly?  DateTo,
    string?    BranchCode,
    string?    SeriesCode,
    string?    TourGenericCode,
    string?    DestinationCode,
    string?    OpsManager,
    string?    OpsExec,
    string?    Search,
    DateOnly?  WindowStart = null);

// -------------------------------------------------------------------------
// Shared row records
// -------------------------------------------------------------------------
internal sealed record DepartureRow(
    Guid           Id,
    string         TenantId,
    string         DepartureId,
    string         SeriesCode,
    string         SeriesName,
    DateOnly       DepartureDate,
    DateOnly?      ReturnDate,
    string         DestinationCode,
    string         DestinationName,
    string         BranchCode,
    string         OpsManagerInitials,
    string         OpsManagerName,
    string         OpsExecInitials,
    string         OpsExecName,
    int            PaxCount,
    int            BookingCount,
    bool           Gtd,
    string?        Notes,
    DateTimeOffset UpdatedOn);

internal sealed record GroupTaskRow(
    string         DepartureId,
    string         GroupTaskId,
    string         TemplateCode,
    string         Status,
    DateOnly?      DueDate,
    DateOnly?      CompletedDate,
    string?        Notes,
    string         Source,
    bool           Required,
    DateTimeOffset UpdatedOn);

internal sealed record BusinessRulesRow(
    string         TenantId,
    string         CompanyCode,
    string         BranchCode,
    int            OverdueCriticalDays,
    int            OverdueWarningDays,
    string         ReadinessMethod,
    string         RiskRedThreshold,
    string         RiskAmberThreshold,
    string         RiskGreenThreshold,
    int            HeatmapRedMax,
    int            HeatmapAmberMax,
    bool           AutoMarkOverdue,
    bool           IncludeNaInReadiness,
    DateTimeOffset UpdatedAt,
    string         UpdatedBy);

internal sealed record SlaRuleRow(
    Guid           Id,
    string         Level,
    string         ScopeKey,
    string?        TourCode,
    string         GroupTaskCode,
    string         ReferenceDate,
    int?           GroupTaskSlaOffsetDays,
    string?        Version,
    DateTimeOffset UpdatedOn);
