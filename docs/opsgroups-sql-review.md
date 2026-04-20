# Nova.OpsGroups.Api — SQL Review

All queries target **new tables** (snake_case columns, `opsgroups` schema) unless noted.
No MSSQL-LEGACY markers are present — all `opsgroups.*` tables were created fresh.

The one cross-service query (`TaskTemplatesForTenantSql`) targets `presets.group_task_templates`,
also a new table created by Nova.Presets.Api V004 migration.

Dialect resolution: every helper calls `OpsGroupsDbHelper.Dialect(db.DbType)` → `dialect.TableRef(schema, table)`.
MSSQL emits `[opsgroups].[table]`, Postgres `"opsgroups"."table"`, MariaDB `` `table` `` (schema ignored).

---

## 1. Auth DB — team members lookup

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `TeamMembersQuerySql(AuthDbSettings authDb)`  
**Called from:** `HelloWorldEndpoint` (retained for diagnostics; not part of the public API surface)

```sql
SELECT  r.user_id,
        p.display_name,
        r.role_code,
        r.role_flags
FROM    {nova_auth.user_security_rights}  r
JOIN    {nova_auth.tenant_user_profile}   p ON p.tenant_id = r.tenant_id
                                           AND p.user_id   = r.user_id
WHERE   r.tenant_id    = @TenantId
AND     (r.company_code = @CompanyCode OR r.company_code = 'XXXX')
AND     (r.branch_code  = @BranchCode  OR r.branch_code  = 'XXXX')
AND     r.role_code    IN ('OPSMGR', 'OPSEXEC')
AND     r.frz_ind      = {false}
AND     p.frz_ind      = {false}
ORDER BY p.display_name, r.role_code
```

> **Review note:** `nova_auth.user_security_rights` and `nova_auth.tenant_user_profile` are new tables
> created by Nova.CommonUX.Api. No MSSQL-LEGACY treatment needed.

---

## 2. Departures — list (paginated)

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `DeparturesListSql(OpsGroupsDbSettings db, DepartureFilters f, int skip, int take)`  
**Called from:**
- `DeparturesEndpoint.HandleListAsync` (skip/take from page params, max 500)
- `DashboardEndpoints.HandleSummaryAsync` (skip=0, take=10000)
- `DashboardEndpoints.HandleTasksViewAsync` (skip/take from page params, max 200)
- `DashboardEndpoints.HandleSeriesAggregateAsync` (skip=0, take=5000)
- `DashboardEndpoints.HandleHeatmapAsync` (skip=0, take=5000)

```sql
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
FROM    {opsgroups.grouptour_departures} d
WHERE   d.tenant_id = @TenantId
AND     d.frz_ind   = {false}
[AND    d.departure_date >= @DateFrom]          -- if DateFrom provided
[AND    d.departure_date <= @DateTo]            -- if DateTo provided
[AND    d.branch_code = @BranchCode]            -- if BranchCode provided
[AND    d.series_code = @SeriesCode]            -- if SeriesCode provided
[AND    d.destination_code = @DestinationCode]  -- if DestinationCode provided
[AND    d.ops_manager_initials = @OpsManager]   -- if OpsManager provided
[AND    d.ops_exec_initials = @OpsExec]         -- if OpsExec provided
[AND    (d.series_name LIKE @Search OR d.destination_name LIKE @Search OR d.departure_id LIKE @Search)]
ORDER BY d.departure_date, d.departure_id
{dialect.OffsetFetchClause(skip, take)}
```

---

## 3. Departures — count (same filters, no pagination)

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `DeparturesCountSql(OpsGroupsDbSettings db, DepartureFilters f)`  
**Called from:** `DeparturesEndpoint.HandleListAsync`, `DashboardEndpoints.HandleFacetsAsync`, `DashboardEndpoints.HandleTasksViewAsync`

```sql
SELECT COUNT(*)
FROM   {opsgroups.grouptour_departures} d
WHERE  {same dynamic WHERE as DeparturesListSql}
```

---

## 4. Departures — single by ID

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `DepartureByIdSql(OpsGroupsDbSettings db)`  
**Called from:** `DeparturesEndpoint.HandleDetailAsync`

```sql
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
FROM    {opsgroups.grouptour_departures} d
WHERE   d.tenant_id    = @TenantId
AND     d.departure_id = @DepartureId
AND     d.frz_ind      = {false}
```

---

## 5. Group tasks — by departure IDs (IN list)

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `GroupTasksByDepartureIdsSql(OpsGroupsDbSettings db)`  
**Called from:** All departure list/detail/dashboard handlers that need task data; Dapper expands `@DepartureIds` from `IEnumerable<string>`.

```sql
SELECT  t.departure_id,
        t.group_task_id,
        t.template_code,
        t.status,
        t.due_date,
        t.completed_date,
        t.notes,
        t.source,
        t.updated_on
FROM    {opsgroups.grouptour_departure_group_tasks} t
WHERE   t.tenant_id    = @TenantId
AND     t.departure_id IN @DepartureIds
AND     t.frz_ind      = {false}
ORDER BY t.template_code
```

---

## 6. Group task — single UPDATE

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `GroupTaskUpdateSql(OpsGroupsDbSettings db)`  
**Called from:**
- `GroupTaskEndpoint.HandleSingleUpdateAsync`
- `GroupTaskEndpoint.HandleBulkUpdateAsync` (inside transaction, per item)

```sql
UPDATE {opsgroups.grouptour_departure_group_tasks}
SET    status         = @Status,
       notes          = @Notes,
       completed_date = @CompletedDate,
       updated_on     = @Now,
       updated_by     = @UpdatedBy,
       updated_at     = 'Nova.OpsGroups.Api'
WHERE  tenant_id      = @TenantId
AND    departure_id   = @DepartureId
AND    group_task_id  = @GroupTaskId
```

---

## 7. Group task — fetch by ID (post-update read-back)

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `GroupTaskByIdSql(OpsGroupsDbSettings db)`  
**Called from:** `GroupTaskEndpoint.HandleSingleUpdateAsync` (read-back after update)

```sql
SELECT  departure_id,
        group_task_id,
        template_code,
        status,
        due_date,
        completed_date,
        notes,
        source,
        updated_on
FROM    {opsgroups.grouptour_departure_group_tasks}
WHERE   tenant_id     = @TenantId
AND     departure_id  = @DepartureId
AND     group_task_id = @GroupTaskId
AND     frz_ind       = {false}
```

---

## 8. Group task — current status (optimistic lock check)

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `GroupTaskCurrentStatusSql(OpsGroupsDbSettings db)`  
**Called from:** `GroupTaskEndpoint.HandleBulkUpdateAsync` (per item, inside transaction before update)

```sql
SELECT  departure_id,
        group_task_id,
        status
FROM    {opsgroups.grouptour_departure_group_tasks}
WHERE   tenant_id     = @TenantId
AND     departure_id  = @DepartureId
AND     group_task_id = @GroupTaskId
AND     frz_ind       = {false}
```

---

## 9. SLA rules — list (by tenant)

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SlaRulesListSql(OpsGroupsDbSettings db)`  
**Called from:** `SlaRulesEndpoint.HandleFetchAsync`, `SlaRulesEndpoint.HandleSaveAsync` (read-back after save)

```sql
SELECT  id,
        level,
        scope_key,
        tour_code,
        group_task_code,
        reference_date,
        group_task_sla_offset_days,
        version,
        updated_on
FROM    {opsgroups.grouptour_sla_rules}
WHERE   tenant_id = @TenantId
ORDER BY level, group_task_code, reference_date
```

---

## 10. SLA rule — UPSERT (3-dialect)

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SlaRuleUpsertSql(OpsGroupsDbSettings db)`  
**Called from:** `SlaRulesEndpoint.HandleSaveAsync`, `SlaHierarchyEndpoint.HandleRuleSaveAsync`  
**Unique key:** `(tenant_id, scope_key, group_task_code, reference_date)`

### MSSQL
```sql
MERGE INTO {opsgroups.grouptour_sla_rules} WITH (HOLDLOCK) AS target
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
```

### Postgres
```sql
INSERT INTO {opsgroups.grouptour_sla_rules}
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
```

### MariaDB
```sql
INSERT INTO `grouptour_sla_rules`
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
```

---

## 11. SLA rules — all for tenant (hierarchy build)

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SlaRulesForTenantSql(OpsGroupsDbSettings db)`  
**Called from:** `SlaHierarchyEndpoint.HandleHierarchyAsync`  
**Note:** Fetches all levels; tree built in C#.

```sql
SELECT  id,
        level,
        scope_key,
        tour_code,
        group_task_code,
        reference_date,
        group_task_sla_offset_days,
        version,
        updated_on
FROM    {opsgroups.grouptour_sla_rules}
WHERE   tenant_id = @TenantId
ORDER BY level, scope_key, group_task_code, reference_date
```

---

## 12. SLA rule — optimistic check (inline in HandleRuleSaveAsync)

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/SlaRules/SlaHierarchyEndpoint.cs`  
**Method:** `HandleRuleSaveAsync` (inline SQL, not extracted to helper)  
**Note:** Runs per change item inside a transaction. If current value ≠ old_value → 409 + rollback.

```sql
SELECT group_task_sla_offset_days AS GroupTaskSlaOffsetDays,
       id AS Id, level AS Level,
       scope_key AS ScopeKey, tour_code AS TourCode, group_task_code AS GroupTaskCode,
       reference_date AS ReferenceDate, version AS Version, updated_on AS UpdatedOn
FROM   {opsgroups.grouptour_sla_rules}
WHERE  tenant_id       = @TenantId
AND    scope_key       = @ScopeKey
AND    group_task_code = @GroupTaskCode
AND    reference_date  = @ReferenceDate
```

> **Review note:** This query uses explicit column aliases (`AS GroupTaskSlaOffsetDays` etc.) because it is
> written inline rather than going through a shared helper. The aliases are required for Dapper to map
> to `SlaRuleRow` when `MatchNamesWithUnderscores` alone would not suffice for this particular SELECT list.

---

## 13. SLA rule — DELETE (clear series-level override)

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SlaRuleDeleteSql(OpsGroupsDbSettings db)`  
**Called from:** `SlaHierarchyEndpoint.HandleRuleSaveAsync` when `new_value = null` and `level_type != "global"`

```sql
DELETE FROM {opsgroups.grouptour_sla_rules}
WHERE   tenant_id       = @TenantId
AND     scope_key       = @ScopeKey
AND     group_task_code = @GroupTaskCode
AND     reference_date  = @ReferenceDate
```

---

## 14. SLA audit — INSERT

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SlaAuditInsertSql(OpsGroupsDbSettings db)`  
**Called from:** `SlaHierarchyEndpoint.HandleRuleSaveAsync` after each change (inside same transaction)

```sql
INSERT INTO {opsgroups.grouptour_sla_rule_audit}
    (id, tenant_id, scope_key, scope_label, group_task_code, reference_date,
     old_value, new_value, changed_by_name, changed_at)
VALUES
    (@Id, @TenantId, @ScopeKey, @ScopeLabel, @GroupTaskCode, @ReferenceDate,
     @OldValue, @NewValue, @ChangedByName, @ChangedAt)
```

---

## 15. SLA audit — query (paginated in C#)

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SlaAuditQuerySql(OpsGroupsDbSettings db)`  
**Called from:** `SlaHierarchyEndpoint.HandleAuditAsync`  
**Note:** Full result set is fetched; `Skip/Take` applied in C# after fetch.
Consider adding server-side `OffsetFetchClause` if audit logs grow large.

```sql
SELECT  id,
        scope_key,
        scope_label,
        group_task_code,
        reference_date,
        old_value,
        new_value,
        changed_by_name,
        changed_at
FROM    {opsgroups.grouptour_sla_rule_audit}
WHERE   tenant_id = @TenantId
AND     scope_key = @ScopeKey
ORDER BY changed_at DESC
```

---

## 16. SLA audit — count

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SlaAuditCountSql(OpsGroupsDbSettings db)`  
**Called from:** `SlaHierarchyEndpoint.HandleAuditAsync`

```sql
SELECT COUNT(*)
FROM   {opsgroups.grouptour_sla_rule_audit}
WHERE  tenant_id = @TenantId
AND    scope_key = @ScopeKey
```

---

## 17. Business rules — fetch

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `BusinessRulesFetchSql(OpsGroupsDbSettings db)`  
**Called from:** `BusinessRulesEndpoint.HandleFetchAsync`, `BusinessRulesEndpoint.HandleSaveAsync` (read-before-write)

```sql
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
FROM    {opsgroups.grouptour_task_business_rules}
WHERE   tenant_id    = @TenantId
AND     company_code = @CompanyCode
AND     branch_code  = @BranchCode
```

---

## 18. Business rules — UPSERT (3-dialect)

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `BusinessRulesUpsertSql(OpsGroupsDbSettings db)`  
**Called from:** `BusinessRulesEndpoint.HandleSaveAsync`  
**Unique key:** `(tenant_id, company_code, branch_code)` — composite PK, no `id` column on this table.

### MSSQL
```sql
MERGE INTO {opsgroups.grouptour_task_business_rules} WITH (HOLDLOCK) AS target
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
```

### Postgres
```sql
INSERT INTO {opsgroups.grouptour_task_business_rules}
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
```

### MariaDB
```sql
INSERT INTO `grouptour_task_business_rules`
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
```

---

## 19. Summary stats — correlated subqueries

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SummaryStatsSql(OpsGroupsDbSettings db)`  
**Called from:** `SummaryStatsEndpoint.HandleAsync`  
**Parameters:** `@TenantId, @DateFrom, @DateTo, @Today, @WeekEnd, @TodayUtc`

```sql
SELECT
    (SELECT COUNT(*) FROM {opsgroups.grouptour_departures} d
     WHERE d.tenant_id = @TenantId AND d.frz_ind = {false}
     AND d.departure_date >= @DateFrom AND d.departure_date <= @DateTo) AS TotalDepartures,

    (SELECT COUNT(*) FROM {opsgroups.grouptour_departure_group_tasks} t
     WHERE t.tenant_id = @TenantId AND t.frz_ind = {false}
     AND t.status = 'overdue'
     AND t.departure_id IN (
         SELECT d2.departure_id FROM {opsgroups.grouptour_departures} d2
         WHERE d2.tenant_id = @TenantId AND d2.frz_ind = {false}
         AND d2.departure_date >= @DateFrom AND d2.departure_date <= @DateTo
     )) AS OverdueGroupTasks,

    (SELECT COUNT(*) FROM {opsgroups.grouptour_departure_group_tasks} t
     WHERE t.tenant_id = @TenantId AND t.frz_ind = {false}
     AND t.due_date >= @Today AND t.due_date <= @WeekEnd
     AND t.status NOT IN ('complete', 'not_applicable')
     AND t.departure_id IN (
         SELECT d2.departure_id FROM {opsgroups.grouptour_departures} d2
         WHERE d2.tenant_id = @TenantId AND d2.frz_ind = {false}
         AND d2.departure_date >= @DateFrom AND d2.departure_date <= @DateTo
     )) AS DueThisWeek,

    (SELECT COUNT(*) FROM {opsgroups.grouptour_departure_group_tasks} t
     WHERE t.tenant_id = @TenantId AND t.frz_ind = {false}
     AND t.status = 'complete'
     AND t.updated_on >= @TodayUtc
     AND t.departure_id IN (
         SELECT d2.departure_id FROM {opsgroups.grouptour_departures} d2
         WHERE d2.tenant_id = @TenantId AND d2.frz_ind = {false}
         AND d2.departure_date >= @DateFrom AND d2.departure_date <= @DateTo
     )) AS CompletedToday
```

> **Review note:** `readiness_avg_pct` is always returned as `0` — the `SummaryStatsEndpoint.HandleAsync`
> has a placeholder `double readinessAvg = stats.TotalDepartures == 0 ? 0 : 0;`.
> If average readiness is needed, consider either a second query or computing it post-query from a full
> departure+task fetch (as `DashboardEndpoints.HandleSummaryAsync` does).

---

## 20. Facets — DISTINCT branches / managers / execs / series

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `FacetsSql(OpsGroupsDbSettings db, DepartureFilters f)`  
**Called from:** `DashboardEndpoints.HandleFacetsAsync`

```sql
SELECT DISTINCT
    d.branch_code,
    d.ops_manager_initials,
    d.ops_manager_name,
    d.ops_exec_initials,
    d.ops_exec_name,
    d.series_code,
    d.series_name
FROM {opsgroups.grouptour_departures} d
WHERE {same dynamic WHERE as DeparturesListSql}
ORDER BY d.branch_code, d.series_code
```

---

## 21. Series aggregate — GROUP BY series

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SeriesAggregateSql(OpsGroupsDbSettings db, DepartureFilters f)`  
**Called from:** `DashboardEndpoints.HandleSeriesAggregateAsync`

```sql
SELECT  d.series_code,
        d.series_name,
        SUM(d.pax_count)      AS TotalPax,
        COUNT(d.departure_id) AS TotalDepartures
FROM    {opsgroups.grouptour_departures} d
WHERE   {same dynamic WHERE as DeparturesListSql}
GROUP BY d.series_code, d.series_name
ORDER BY d.series_code
```

---

## 22. Heatmap — DISTINCT departure dates (capped at 28)

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `HeatmapDatesSql(OpsGroupsDbSettings db, DepartureFilters f)`  
**Called from:** `DashboardEndpoints.HandleHeatmapAsync`  
**Note:** `dialect.OffsetFetchClause(0, 28)` caps the date window at 28 dates. `@WindowStart` filter is always added.

```sql
SELECT DISTINCT d.departure_date
FROM    {opsgroups.grouptour_departures} d
WHERE   d.tenant_id = @TenantId
AND     d.frz_ind   = {false}
[AND    d.departure_date >= @DateFrom]
[AND    d.departure_date <= @DateTo]
[AND    d.branch_code = @BranchCode]
[AND    d.series_code = @SeriesCode]
[AND    d.destination_code = @DestinationCode]
[AND    d.ops_manager_initials = @OpsManager]
[AND    d.ops_exec_initials = @OpsExec]
AND     d.departure_date >= @WindowStart        -- always present
ORDER BY d.departure_date
{dialect.OffsetFetchClause(0, 28)}
```

---

## 23. Cross-service: task template codes (queries PresetsDb)

**File:** `src/services/Nova.OpsGroups.Api/Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `TaskTemplatesForTenantSql(DbType dbType)`  
**Called from:** `SlaHierarchyEndpoint.HandleCodesAvailableAsync`  
**Connection:** Uses `PresetsDbSettings` (separate DB from OpsGroupsDb)  
**Target table:** `presets.group_task_templates` — created by Nova.Presets.Api V004 migration

```sql
SELECT  code,
        name,
        source
FROM    {presets.group_task_templates}
WHERE   tenant_id = @TenantId
AND     frz_ind   = {false}
ORDER BY code
```

---

## Summary of flags for your attention

| # | Issue | Location |
|---|-------|----------|
| 1 | `readiness_avg_pct` always returns `0` | `SummaryStatsEndpoint.HandleAsync` line 62 |
| 2 | SLA audit pagination done in C# (full table fetch) | `SlaHierarchyEndpoint.HandleAuditAsync` — fine for small volumes, consider server-side if audit grows |
| 3 | Inline SQL in `HandleRuleSaveAsync` uses explicit aliases | `SlaHierarchyEndpoint.cs:150–163` — intentional; extract to helper if reused |
| 4 | `DashboardEndpoints.HandleSummaryAsync` fetches up to 10,000 departures in memory | acceptable for expected data volumes; monitor query time if tenant data grows |
