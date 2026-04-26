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

    // Builds shared WHERE predicates for all departure queries.
    // BranchCodeFilter (array) takes precedence over single BranchCode.
    // TourGenericCode requires a series→generic subquery since tour_departures has no tg column.
    private static List<string> DepartureWhereFilters(DepartureFilters f, ISqlDialect dialect)
    {
        string falsy = dialect.BooleanLiteral(false);
        var where = new List<string>
        {
            "d.tenant_id = @TenantId",
            $"d.frz_ind  = {falsy}",
        };

        if (f.DateFrom.HasValue)   where.Add("d.departure_date >= @DateFrom");
        if (f.DateTo.HasValue)     where.Add("d.departure_date <= @DateTo");

        if (f.BranchCodeFilter is { Length: > 0 })
            where.Add("d.branch_code IN @BranchCodeFilter");
        else if (!string.IsNullOrEmpty(f.BranchCode))
            where.Add("d.branch_code = @BranchCode");

        if (!string.IsNullOrEmpty(f.SeriesCode))      where.Add("d.series_code = @SeriesCode");
        if (!string.IsNullOrEmpty(f.DestinationCode)) where.Add("d.destination_code = @DestinationCode");
        if (!string.IsNullOrEmpty(f.OpsManager))      where.Add("d.ops_manager_initials = @OpsManager");
        if (!string.IsNullOrEmpty(f.OpsExec))         where.Add("d.ops_exec_initials = @OpsExec");
        if (!string.IsNullOrEmpty(f.Search))          where.Add("(d.series_name LIKE @Search OR d.destination_name LIKE @Search OR d.departure_id LIKE @Search)");

        if (!string.IsNullOrEmpty(f.TourGenericCode))
        {
            string ts = dialect.TableRef("presets", "tour_series");
            string tg = dialect.TableRef("presets", "tour_generics");
            where.Add($"d.series_code IN (SELECT s.series_code FROM {ts} s JOIN {tg} g ON g.id = s.tour_generic_id AND g.tenant_id = s.tenant_id WHERE s.tenant_id = @TenantId AND g.code = @TourGenericCode)");
        }

        return where;
    }

    internal static string DeparturesListSql(OpsGroupsDbSettings db, DepartureFilters f, int skip, int take)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("presets", "tour_departures");
        string      paging  = dialect.OffsetFetchClause(skip, take);
        string      where   = string.Join(" AND ", DepartureWhereFilters(f, dialect));

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
                WHERE   {where}
                ORDER BY d.departure_date, d.departure_id
                {paging}
                """;
    }

    internal static string DeparturesCountSql(OpsGroupsDbSettings db, DepartureFilters f)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("presets", "tour_departures");
        string      where   = string.Join(" AND ", DepartureWhereFilters(f, dialect));

        return $"SELECT COUNT(*) FROM {table} d WHERE {where}";
    }

    internal static string DepartureByIdSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("presets", "tour_departures");
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
        string      table   = dialect.TableRef("presets", "grouptour_departure_group_tasks");
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
                        t.lock_ver,
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
        string      table   = dialect.TableRef("presets", "grouptour_departure_group_tasks");

        return $"""
                UPDATE {table}
                SET    status         = @Status,
                       notes          = @Notes,
                       completed_date = @CompletedDate,
                       updated_on     = @Now,
                       updated_by     = @UpdatedBy,
                       updated_at     = 'Nova.OpsGroups.Api',
                       lock_ver       = lock_ver + 1
                WHERE  tenant_id      = @TenantId
                AND    departure_id   = @DepartureId
                AND    group_task_id  = @GroupTaskId
                AND    lock_ver       = @ExpectedLockVer
                """;
    }

    internal static string GroupTaskByIdSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("presets", "grouptour_departure_group_tasks");
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
                        lock_ver,
                        updated_on
                FROM    {table}
                WHERE   tenant_id     = @TenantId
                AND     departure_id  = @DepartureId
                AND     group_task_id = @GroupTaskId
                AND     frz_ind       = {falsy}
                """;
    }

    internal static string GroupTaskConditionalUpdateSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("presets", "grouptour_departure_group_tasks");

        return $"""
                UPDATE {table}
                SET    status         = @Status,
                       notes          = @Notes,
                       completed_date = @CompletedDate,
                       updated_on     = @Now,
                       updated_by     = @UpdatedBy,
                       updated_at     = 'Nova.OpsGroups.Api',
                       lock_ver       = lock_ver + 1
                WHERE  tenant_id      = @TenantId
                AND    departure_id   = @DepartureId
                AND    group_task_id  = @GroupTaskId
                AND    lock_ver       = @ExpectedLockVer
                """;
    }

    internal static string GroupTaskExistsSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("presets", "grouptour_departure_group_tasks");
        string      falsy   = dialect.BooleanLiteral(false);

        return $"""
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM {table}
                    WHERE  tenant_id     = @TenantId
                    AND    departure_id  = @DepartureId
                    AND    group_task_id = @GroupTaskId
                    AND    frz_ind       = {falsy}
                ) THEN 1 ELSE 0 END
                """;
    }

    // -------------------------------------------------------------------------
    // SLA scope resolution
    // -------------------------------------------------------------------------

    internal static string ResolveGlobalScopeSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      tg      = dialect.TableRef("presets", "tour_generics");
        return $"SELECT id AS GlobId FROM {tg} WHERE tenant_id = @TenantId AND code = 'GLOBAL'";
    }

    internal static string ResolveTourGenericScopeSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      tg      = dialect.TableRef("presets", "tour_generics");
        return $"""
                SELECT tg.id AS TgId, glob.id AS GlobId
                FROM   {tg} tg
                JOIN   {tg} glob ON glob.tenant_id = tg.tenant_id AND glob.code = 'GLOBAL'
                WHERE  tg.tenant_id = @TenantId AND tg.code = @TourGenericCode
                """;
    }

    internal static string ResolveTourSeriesScopeSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      ts      = dialect.TableRef("presets", "tour_series");
        string      tg      = dialect.TableRef("presets", "tour_generics");
        return $"""
                SELECT ts.id AS TsId, ts.tour_generic_id AS TgId, glob.id AS GlobId
                FROM   {ts} ts
                JOIN   {tg} glob ON glob.tenant_id = ts.tenant_id AND glob.code = 'GLOBAL'
                WHERE  ts.tenant_id = @TenantId AND ts.series_code = @SeriesCode
                """;
    }

    internal static string ResolveTourDepartureScopeSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      td      = dialect.TableRef("presets", "tour_departures");
        string      ts      = dialect.TableRef("presets", "tour_series");
        string      tg      = dialect.TableRef("presets", "tour_generics");
        return $"""
                SELECT td.id AS TdId, ts.id AS TsId, ts.tour_generic_id AS TgId, glob.id AS GlobId
                FROM   {td} td
                JOIN   {ts} ts   ON ts.tenant_id  = td.tenant_id AND ts.series_code = td.series_code
                JOIN   {tg} glob ON glob.tenant_id = td.tenant_id AND glob.code = 'GLOBAL'
                WHERE  td.tenant_id = @TenantId AND td.departure_id = @DepartureId
                """;
    }

    // -------------------------------------------------------------------------
    // SLA task (normalised cell store)
    // -------------------------------------------------------------------------

    internal static string SlaTaskFetchForScopesSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      table   = dialect.TableRef("presets", "sla_task");
        return $"""
                SELECT id, tenant_id, scope_type, scope_id, enq_event_code, task_code,
                       kind, offset_days, updated_by, updated_on
                FROM   {table}
                WHERE  tenant_id = @TenantId AND scope_id IN @ScopeIds
                """;
    }

    internal static string SlaTaskUpsertSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      table   = dialect.TableRef("presets", "sla_task");

        return dbType switch
        {
            DbType.MsSql => $"""
                             MERGE INTO {table} WITH (HOLDLOCK) AS target
                             USING (SELECT @TenantId AS tenant_id, @ScopeType AS scope_type,
                                          @ScopeId AS scope_id, @EnqEventCode AS enq_event_code,
                                          @TaskCode AS task_code) AS source
                                   ON target.tenant_id      = source.tenant_id
                                  AND target.scope_type     = source.scope_type
                                  AND target.scope_id       = source.scope_id
                                  AND target.enq_event_code = source.enq_event_code
                                  AND target.task_code      = source.task_code
                             WHEN MATCHED THEN
                                 UPDATE SET kind        = @Kind,
                                            offset_days = @OffsetDays,
                                            updated_by  = @UpdatedBy,
                                            updated_on  = @Now
                             WHEN NOT MATCHED THEN
                                 INSERT (id, tenant_id, scope_type, scope_id, enq_event_code,
                                         task_code, kind, offset_days, updated_by, updated_on)
                                 VALUES (@Id, @TenantId, @ScopeType, @ScopeId, @EnqEventCode,
                                         @TaskCode, @Kind, @OffsetDays, @UpdatedBy, @Now);
                             """,

            DbType.Postgres => $"""
                                INSERT INTO {table}
                                    (id, tenant_id, scope_type, scope_id, enq_event_code,
                                     task_code, kind, offset_days, updated_by, updated_on)
                                VALUES
                                    (@Id, @TenantId, @ScopeType, @ScopeId, @EnqEventCode,
                                     @TaskCode, @Kind, @OffsetDays, @UpdatedBy, @Now)
                                ON CONFLICT (tenant_id, scope_type, scope_id, enq_event_code, task_code) DO UPDATE SET
                                    kind        = EXCLUDED.kind,
                                    offset_days = EXCLUDED.offset_days,
                                    updated_by  = EXCLUDED.updated_by,
                                    updated_on  = EXCLUDED.updated_on
                                """,

            _ /* MariaDB */ => $"""
                                INSERT INTO {table}
                                    (`id`, `tenant_id`, `scope_type`, `scope_id`, `enq_event_code`,
                                     `task_code`, `kind`, `offset_days`, `updated_by`, `updated_on`)
                                VALUES
                                    (@Id, @TenantId, @ScopeType, @ScopeId, @EnqEventCode,
                                     @TaskCode, @Kind, @OffsetDays, @UpdatedBy, @Now)
                                ON DUPLICATE KEY UPDATE
                                    `kind`        = VALUES(`kind`),
                                    `offset_days` = VALUES(`offset_days`),
                                    `updated_by`  = VALUES(`updated_by`),
                                    `updated_on`  = VALUES(`updated_on`)
                                """
        };
    }

    internal static string SlaTaskDeleteSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      table   = dialect.TableRef("presets", "sla_task");
        return $"""
                DELETE FROM {table}
                WHERE  tenant_id      = @TenantId
                AND    scope_type     = @ScopeType
                AND    scope_id       = @ScopeId
                AND    enq_event_code = @EnqEventCode
                AND    task_code      = @TaskCode
                """;
    }

    internal static string SlaTaskAuditInsertSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      table   = dialect.TableRef("presets", "sla_task_audit");
        return $"""
                INSERT INTO {table}
                    (id, tenant_id, scope_type, scope_id, enq_event_code, task_code,
                     kind_old, offset_days_old, kind_new, offset_days_new, changed_by, changed_on)
                VALUES
                    (@Id, @TenantId, @ScopeType, @ScopeId, @EnqEventCode, @TaskCode,
                     @KindOld, @OffsetDaysOld, @KindNew, @OffsetDaysNew, @ChangedBy, @Now)
                """;
    }

    internal static string SlaTaskAuditFetchSql(DbType dbType, int skip, int take)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      table   = dialect.TableRef("presets", "sla_task_audit");
        string      paging  = dialect.OffsetFetchClause(skip, take);
        return $"""
                SELECT id, scope_type, scope_id, enq_event_code, task_code,
                       kind_old, offset_days_old, kind_new, offset_days_new, changed_by, changed_on
                FROM   {table}
                WHERE  tenant_id = @TenantId AND scope_id = @ScopeId
                ORDER BY changed_on DESC
                {paging}
                """;
    }

    internal static string SlaTaskAuditCountSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      table   = dialect.TableRef("presets", "sla_task_audit");
        return $"SELECT COUNT(*) FROM {table} WHERE tenant_id = @TenantId AND scope_id = @ScopeId";
    }

    internal static string EnquiryEventsSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      table   = dialect.TableRef("presets", "enquiry_events");
        return $"SELECT code, description, sort_order FROM {table} ORDER BY sort_order";
    }

    // -------------------------------------------------------------------------
    // SLA hierarchy tree — TG + series + departure fetches
    // -------------------------------------------------------------------------

    internal static string FetchTourGenericWithGlobSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      tg      = dialect.TableRef("presets", "tour_generics");
        return $"""
                SELECT tg.id AS TgId, tg.code AS Code, tg.name AS Name, glob.id AS GlobId
                FROM   {tg} tg
                JOIN   {tg} glob ON glob.tenant_id = tg.tenant_id AND glob.code = 'GLOBAL'
                WHERE  tg.tenant_id = @TenantId AND tg.code = @TourGenericCode
                """;
    }

    internal static string FetchGlobalInfoSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      tg      = dialect.TableRef("presets", "tour_generics");
        return $"SELECT id AS GlobId, name AS Name FROM {tg} WHERE tenant_id = @TenantId AND code = 'GLOBAL'";
    }

    internal static string FetchSeriesForTgSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      ts      = dialect.TableRef("presets", "tour_series");
        string      falsy   = dialect.BooleanLiteral(false);
        return $"""
                SELECT id AS Id, series_code AS SeriesCode, series_name AS SeriesName
                FROM   {ts}
                WHERE  tenant_id = @TenantId AND tour_generic_id = @TgId AND frz_ind = {falsy}
                ORDER  BY series_code
                """;
    }

    internal static string FetchDeparturesForTgSql(DbType dbType)
    {
        ISqlDialect dialect = Dialect(dbType);
        string      td      = dialect.TableRef("presets", "tour_departures");
        string      ts      = dialect.TableRef("presets", "tour_series");
        string      falsy   = dialect.BooleanLiteral(false);
        return $"""
                SELECT td.id AS Id, ts.id AS TsId, td.series_code AS SeriesCode, td.departure_date AS DepartureDate
                FROM   {td} td
                JOIN   {ts} ts ON ts.tenant_id = td.tenant_id AND ts.series_code = td.series_code
                WHERE  ts.tenant_id      = @TenantId
                AND    ts.tour_generic_id = @TgId
                AND    td.departure_date >= @YearFloorDate
                AND    td.frz_ind         = {falsy}
                ORDER  BY td.departure_date, td.series_code
                """;
    }

    // -------------------------------------------------------------------------
    // Business rules
    // -------------------------------------------------------------------------

    internal static string BusinessRulesFetchSql(OpsGroupsDbSettings db)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("presets", "grouptour_task_business_rules");

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
        string      table   = dialect.TableRef("presets", "grouptour_task_business_rules");

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
        string      depTable = dialect.TableRef("presets", "tour_departures");
        string      taskTable = dialect.TableRef("presets", "grouptour_departure_group_tasks");
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
        string      table   = dialect.TableRef("presets", "tour_departures");
        string      where   = string.Join(" AND ", DepartureWhereFilters(f, dialect));

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
                WHERE {where}
                ORDER BY d.branch_code, d.series_code
                """;
    }

    internal static string FacetsTourGenericsSql(OpsGroupsDbSettings db, DepartureFilters f)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("presets", "tour_departures");
        string      ts      = dialect.TableRef("presets", "tour_series");
        string      tg      = dialect.TableRef("presets", "tour_generics");
        string      falsy   = dialect.BooleanLiteral(false);

        var whereClauses = DepartureWhereFilters(f, dialect);
        whereClauses.Add($"g.frz_ind = {falsy}");
        string where = string.Join(" AND ", whereClauses);

        return $"""
                SELECT DISTINCT g.code AS TourGenericCode, g.name AS TourGenericName
                FROM   {table} d
                JOIN   {ts}    s ON s.series_code = d.series_code AND s.tenant_id = d.tenant_id
                JOIN   {tg}    g ON g.id           = s.tour_generic_id AND g.tenant_id = d.tenant_id
                WHERE  {where}
                ORDER BY g.name
                """;
    }

    internal static string SeriesAggregateSql(OpsGroupsDbSettings db, DepartureFilters f)
    {
        ISqlDialect dialect = Dialect(db.DbType);
        string      table   = dialect.TableRef("presets", "tour_departures");
        string      where   = string.Join(" AND ", DepartureWhereFilters(f, dialect));

        return $"""
                SELECT  d.series_code,
                        d.series_name,
                        SUM(d.pax_count)      AS TotalPax,
                        COUNT(d.departure_id) AS TotalDepartures
                FROM    {table} d
                WHERE   {where}
                GROUP BY d.series_code, d.series_name
                ORDER BY d.series_code
                """;
    }

    internal static string HeatmapDatesSql(OpsGroupsDbSettings db, DepartureFilters f)
    {
        ISqlDialect dialect      = Dialect(db.DbType);
        string      table        = dialect.TableRef("presets", "tour_departures");
        string      paging       = dialect.OffsetFetchClause(0, 28);
        var         whereClauses = DepartureWhereFilters(f, dialect);
        if (f.WindowStart.HasValue) whereClauses.Add("d.departure_date >= @WindowStart");
        string where = string.Join(" AND ", whereClauses);

        return $"""
                SELECT DISTINCT d.departure_date
                FROM    {table} d
                WHERE   {where}
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
        string      table   = dialect.TableRef("presets", "group_tasks");
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

    // readinessMethod: "required_only" → Completed Required / Total Required
    //                  "all_tasks"     → Completed All / Total All
    // includeNaInReadiness: when true, "not_applicable" tasks count as complete in both modes.
    internal static int ComputeReadinessPct(
        IEnumerable<GroupTaskRow> tasks,
        string readinessMethod      = "required_only",
        bool   includeNaInReadiness = false)
    {
        var list = tasks.ToList();
        if (list.Count == 0) return 0;

        bool IsDone(GroupTaskRow t) =>
            t.Status == "complete" || (includeNaInReadiness && t.Status == "not_applicable");

        if (readinessMethod == "required_only")
        {
            var required = list.Where(t => t.Required).ToList();
            if (required.Count == 0) return 100; // no required tasks → fully ready
            int done = required.Count(IsDone);
            return (int)Math.Round(done / (double)required.Count * 100, MidpointRounding.AwayFromZero);
        }

        // all_tasks: exclude not_applicable from denominator unless includeNaInReadiness
        var eligible = list.Where(t => includeNaInReadiness || t.Status != "not_applicable").ToList();
        if (eligible.Count == 0) return 100;
        int doneAll = eligible.Count(IsDone);
        return (int)Math.Round(doneAll / (double)eligible.Count * 100, MidpointRounding.AwayFromZero);
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
    DateOnly?  WindowStart      = null,
    string[]?  BranchCodeFilter = null);

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
    int            LockVer,
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

// -------------------------------------------------------------------------
// SLA row records
// -------------------------------------------------------------------------
internal sealed record SlaTaskRow(
    Guid           Id,
    string         TenantId,
    string         ScopeType,
    Guid           ScopeId,
    string         EnqEventCode,
    string         TaskCode,
    string         Kind,
    int?           OffsetDays,
    string         UpdatedBy,
    DateTimeOffset UpdatedOn);

internal sealed record SlaAuditRow(
    Guid           Id,
    string         ScopeType,
    Guid           ScopeId,
    string         EnqEventCode,
    string         TaskCode,
    string?        KindOld,
    int?           OffsetDaysOld,
    string?        KindNew,
    int?           OffsetDaysNew,
    string         ChangedBy,
    DateTimeOffset ChangedOn);

internal sealed record EnquiryEventRow(string Code, string Description, int SortOrder);

// Scope resolution rows
internal sealed record ScopeResolutionGlob(Guid GlobId);
internal sealed record ScopeResolutionTg(Guid TgId, Guid GlobId);
internal sealed record ScopeResolutionTs(Guid TsId, Guid TgId, Guid GlobId);
internal sealed record ScopeResolutionTd(Guid TdId, Guid TsId, Guid TgId, Guid GlobId);

// SLA hierarchy tree rows
internal sealed record TourGenericHierarchyRow(Guid TgId, string Code, string Name, Guid GlobId);
internal sealed record GlobalInfoRow(Guid GlobId, string Name);
internal sealed record TourSeriesHierarchyRow(Guid Id, string SeriesCode, string SeriesName);
internal sealed record TourDepartureHierarchyRow(Guid Id, Guid TsId, string SeriesCode, DateOnly DepartureDate);
