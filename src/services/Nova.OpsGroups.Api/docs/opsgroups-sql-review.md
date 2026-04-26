# Nova.OpsGroups.Api — SQL Review

All queries target **new tables** (snake_case columns, `opsgroups` schema) unless noted.
No MSSQL-LEGACY markers are present — all `opsgroups.*` tables were created fresh.

The one cross-service query (`TaskTemplatesForTenantSql`) targets `presets.group_tasks`,
also a new table created by Nova.Presets.Api V004 migration.

---

## Table naming by dialect

| Dialect | `opsgroups` tables | `nova_auth` tables | `presets` tables |
|---|---|---|---|
| Postgres | `"opsgroups"."table_name"` | `"nova_auth"."table_name"` | `"presets"."table_name"` |
| MariaDB | `` `table_name` `` (schema ignored, connect to db directly) | `` `table_name` `` | `` `table_name` `` |
| MSSQL | `[opsgroups].[table_name]` | `[nova_auth].[table_name]` | `[presets].[table_name]` |

> **MSSQL database name:** The schema prefix alone is not sufficient — MSSQL needs a
> three-part name `[database].[schema].[table]` when the connection is not already
> scoped to that database. Replace `[opsgroups]`, `[nova_auth]`, `[presets]` with
> `[actual_db].[opsgroups]`, etc. as appropriate for the target environment.
> All `opsgroups` and `nova_auth` tables are **new** (snake_case) — no MSSQL-LEGACY
> treatment needed.

---

## Flags for your attention

| # | Issue | Location |
|---|---|---|
| 1 | `TeamMembersQuerySql` defined but endpoint not registered in `Program.cs` — route returns 410 | `TeamMembersEndpoint.cs` + `RemovedEndpointsEndpoint.cs` |
| 2 | `readiness_avg_pct` always returns `0` | `SummaryStatsEndpoint.HandleAsync` — placeholder |
| 3 | SLA audit pagination done in C# (full table fetch) | `SlaHierarchyEndpoint.HandleAuditAsync` — fine for small volumes |
| 4 | Inline SQL in `HandleRuleSaveAsync` uses explicit aliases | `SlaHierarchyEndpoint.cs` — intentional; not a helper method |
| 5 | **`required` column missing from V001 migrations** — the SQL in `GroupTasksByDepartureIdsSql` selects `t.required` but no `required` column exists in any `V001__CreateOpsGroups.sql` | V002 migration needed: `ALTER TABLE grouptour_departure_group_tasks ADD required boolean NOT NULL DEFAULT false` (all three dialects) |
| 6 | `tour_generic_code` exists in `DepartureFilters` but has no SQL clause in `DeparturesListSql` | `OpsGroupsDbHelper.cs` — field is passed through but never used in WHERE |

---

## 1. Auth DB — team members lookup

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `TeamMembersQuerySql(AuthDbSettings authDb)`  
**Called from:** `Endpoints/TeamMembers/TeamMembersEndpoint.cs` — `HandleAsync`

> **Note:** `TeamMembersEndpoint.Map(v1)` is **not called** in `Program.cs`. The route
> `POST /api/v1/grouptour-task-team-members` is handled by `RemovedEndpointsEndpoint`
> which returns `410 Gone` with detail "Moved to Nova.Presets.Api: POST /api/v1/users/by-role".
> This SQL is effectively unused in the live service — it remains here as reference.

```sql
SELECT  r.user_id,
        p.display_name,
        r.role_code,
        r.role_flags
FROM    nova_auth.user_security_rights  r
JOIN    nova_auth.tenant_user_profile   p ON p.tenant_id = r.tenant_id
                                         AND p.user_id   = r.user_id
WHERE   r.tenant_id    = @TenantId
AND     (r.company_code = @CompanyCode OR r.company_code = 'XXXX')
AND     (r.branch_code  = @BranchCode  OR r.branch_code  = 'XXXX')
AND     r.role_code    IN ('OPSMGR', 'OPSEXEC')
AND     r.frz_ind      = false
AND     p.frz_ind      = false
ORDER BY p.display_name, r.role_code
```

`nova_auth.user_security_rights` and `nova_auth.tenant_user_profile` are new tables
(snake_case). No MSSQL-LEGACY treatment needed.

---

## 2. Departures — list (paginated)

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `DeparturesListSql(OpsGroupsDbSettings db, DepartureFilters f, int skip, int take)`  
**Called from:**
- `DeparturesEndpoint.HandleListAsync` — `skip/take` from page params, max page_size 500
- `DashboardEndpoints.HandleSummaryAsync` — `skip=0, take=10000`
- `DashboardEndpoints.HandleTasksViewAsync` — `skip/take` from page params, max page_size 200
- `DashboardEndpoints.HandleSeriesAggregateAsync` — `skip=0, take=5000`
- `DashboardEndpoints.HandleHeatmapAsync` — `skip=0, take=5000`

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
FROM    opsgroups.grouptour_departures d
WHERE   d.tenant_id = @TenantId
AND     d.frz_ind   = false
[AND    d.departure_date >= @DateFrom]           -- if DateFrom provided
[AND    d.departure_date <= @DateTo]             -- if DateTo provided
[AND    d.branch_code = @BranchCode]             -- if BranchCode provided
[AND    d.series_code = @SeriesCode]             -- if SeriesCode provided
[AND    d.destination_code = @DestinationCode]   -- if DestinationCode provided
[AND    d.ops_manager_initials = @OpsManager]    -- if OpsManager provided
[AND    d.ops_exec_initials = @OpsExec]          -- if OpsExec provided
[AND    (d.series_name LIKE @Search OR d.destination_name LIKE @Search OR d.departure_id LIKE @Search)]
ORDER BY d.departure_date, d.departure_id
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY    -- MSSQL/Postgres; MariaDB: LIMIT @Take OFFSET @Skip
```

`@Search` is passed as `'%value%'` (already wrapped in the caller).

---

## 3. Departures — count (same filters, no pagination)

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `DeparturesCountSql(OpsGroupsDbSettings db, DepartureFilters f)`  
**Called from:** `DeparturesEndpoint.HandleListAsync`, `DashboardEndpoints.HandleFacetsAsync`, `DashboardEndpoints.HandleTasksViewAsync`

```sql
SELECT COUNT(*)
FROM   opsgroups.grouptour_departures d
WHERE  {same dynamic WHERE as DeparturesListSql, without pagination}
```

---

## 4. Departures — single by departure_id

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
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
FROM    opsgroups.grouptour_departures d
WHERE   d.tenant_id    = @TenantId
AND     d.departure_id = @DepartureId
AND     d.frz_ind      = false
```

---

## 5. Group tasks — by departure IDs (IN list)

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `GroupTasksByDepartureIdsSql(OpsGroupsDbSettings db)`  
**Called from:** All departure list/detail/dashboard handlers that need task data.
Dapper expands `@DepartureIds` from `IEnumerable<string>`.

> **Flag #5:** The column `t.required` is selected here and mapped to `GroupTaskRow.Required`,
> but the `required` column does **not** exist in the `V001__CreateOpsGroups.sql` migration
> for any dialect. A V002 migration is needed before this query will succeed.

```sql
SELECT  t.departure_id,
        t.group_task_id,
        t.template_code,
        t.status,
        t.due_date,
        t.completed_date,
        t.notes,
        t.source,
        t.required,         -- ← requires V002 migration: ADD required boolean NOT NULL DEFAULT false
        t.updated_on
FROM    opsgroups.grouptour_departure_group_tasks t
WHERE   t.tenant_id    = @TenantId
AND     t.departure_id IN @DepartureIds
AND     t.frz_ind      = false
ORDER BY t.template_code
```

---

## 6. Group task — single UPDATE

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `GroupTaskUpdateSql(OpsGroupsDbSettings db)`  
**Called from:**
- `GroupTaskEndpoint.HandleSingleUpdateAsync`
- `GroupTaskEndpoint.HandleBulkUpdateAsync` (inside transaction, per item)

```sql
UPDATE opsgroups.grouptour_departure_group_tasks
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

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
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
FROM    opsgroups.grouptour_departure_group_tasks
WHERE   tenant_id     = @TenantId
AND     departure_id  = @DepartureId
AND     group_task_id = @GroupTaskId
AND     frz_ind       = false
```

---

## 8. Group task — current status (optimistic lock check)

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `GroupTaskCurrentStatusSql(OpsGroupsDbSettings db)`  
**Called from:** `GroupTaskEndpoint.HandleBulkUpdateAsync` (per item, inside transaction before update)

```sql
SELECT  departure_id,
        group_task_id,
        status
FROM    opsgroups.grouptour_departure_group_tasks
WHERE   tenant_id     = @TenantId
AND     departure_id  = @DepartureId
AND     group_task_id = @GroupTaskId
AND     frz_ind       = false
```

---

## 9. SLA rules — list by tenant

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
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
FROM    opsgroups.grouptour_sla_rules
WHERE   tenant_id = @TenantId
ORDER BY level, group_task_code, reference_date
```

---

## 10. SLA rule — UPSERT (3-dialect)

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SlaRuleUpsertSql(OpsGroupsDbSettings db)`  
**Called from:** `SlaRulesEndpoint.HandleSaveAsync`, `SlaHierarchyEndpoint.HandleRuleSaveAsync`  
**Unique key:** `(tenant_id, scope_key, group_task_code, reference_date)`

### MSSQL
```sql
MERGE INTO [opsgroups].[grouptour_sla_rules] WITH (HOLDLOCK) AS target
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
INSERT INTO "opsgroups"."grouptour_sla_rules"
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

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SlaRulesForTenantSql(OpsGroupsDbSettings db)`  
**Called from:** `SlaHierarchyEndpoint.HandleHierarchyAsync`

Fetches all levels; the tree (global + per-series) is built in C# after the single query.

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
FROM    opsgroups.grouptour_sla_rules
WHERE   tenant_id = @TenantId
ORDER BY level, scope_key, group_task_code, reference_date
```

---

## 12. SLA rule — optimistic check (inline in HandleRuleSaveAsync)

**File:** `Endpoints/SlaRules/SlaHierarchyEndpoint.cs`  
**Method:** `HandleRuleSaveAsync` (inline SQL — not extracted to helper)

Runs per change item inside a transaction. If current value ≠ `old_value` in the request → 409 + rollback.

> **Note:** This query uses explicit column aliases (`AS GroupTaskSlaOffsetDays` etc.) because it
> is inline rather than going through a shared helper. Aliases are required for Dapper to map to
> `SlaRuleRow` for this particular SELECT list.

```sql
SELECT  group_task_sla_offset_days AS GroupTaskSlaOffsetDays,
        id                         AS Id,
        level                      AS Level,
        scope_key                  AS ScopeKey,
        tour_code                  AS TourCode,
        group_task_code            AS GroupTaskCode,
        reference_date             AS ReferenceDate,
        version                    AS Version,
        updated_on                 AS UpdatedOn
FROM    opsgroups.grouptour_sla_rules
WHERE   tenant_id       = @TenantId
AND     scope_key       = @ScopeKey
AND     group_task_code = @GroupTaskCode
AND     reference_date  = @ReferenceDate
```

---

## 13. SLA rule — DELETE (clear series-level override)

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SlaRuleDeleteSql(OpsGroupsDbSettings db)`  
**Called from:** `SlaHierarchyEndpoint.HandleRuleSaveAsync` when `new_value = null` and `level_type != "global"`

```sql
DELETE FROM opsgroups.grouptour_sla_rules
WHERE   tenant_id       = @TenantId
AND     scope_key       = @ScopeKey
AND     group_task_code = @GroupTaskCode
AND     reference_date  = @ReferenceDate
```

---

## 14. SLA audit — INSERT

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SlaAuditInsertSql(OpsGroupsDbSettings db)`  
**Called from:** `SlaHierarchyEndpoint.HandleRuleSaveAsync` after each change (inside same transaction)

```sql
INSERT INTO opsgroups.grouptour_sla_rule_audit
    (id, tenant_id, scope_key, scope_label, group_task_code, reference_date,
     old_value, new_value, changed_by_name, changed_at)
VALUES
    (@Id, @TenantId, @ScopeKey, @ScopeLabel, @GroupTaskCode, @ReferenceDate,
     @OldValue, @NewValue, @ChangedByName, @ChangedAt)
```

---

## 15. SLA audit — query (paginated in C#)

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SlaAuditQuerySql(OpsGroupsDbSettings db)`  
**Called from:** `SlaHierarchyEndpoint.HandleAuditAsync`

Full result set is fetched; `Skip/Take` applied in C# after fetch. See flag #3 — consider
adding server-side `OffsetFetchClause` if audit logs grow large.

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
FROM    opsgroups.grouptour_sla_rule_audit
WHERE   tenant_id = @TenantId
AND     scope_key = @ScopeKey
ORDER BY changed_at DESC
```

---

## 16. SLA audit — count

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SlaAuditCountSql(OpsGroupsDbSettings db)`  
**Called from:** `SlaHierarchyEndpoint.HandleAuditAsync`

```sql
SELECT COUNT(*)
FROM   opsgroups.grouptour_sla_rule_audit
WHERE  tenant_id = @TenantId
AND    scope_key = @ScopeKey
```

---

## 17. Business rules — fetch

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `BusinessRulesFetchSql(OpsGroupsDbSettings db)`  
**Called from:** `BusinessRulesEndpoint.HandleFetchAsync`, `BusinessRulesEndpoint.HandleSaveAsync` (read-back after save)

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
FROM    opsgroups.grouptour_task_business_rules
WHERE   tenant_id    = @TenantId
AND     company_code = @CompanyCode
AND     branch_code  = @BranchCode
```

---

## 18. Business rules — UPSERT (3-dialect)

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `BusinessRulesUpsertSql(OpsGroupsDbSettings db)`  
**Called from:** `BusinessRulesEndpoint.HandleSaveAsync`  
**PK (composite — no `id` column):** `(tenant_id, company_code, branch_code)`

### MSSQL
```sql
MERGE INTO [opsgroups].[grouptour_task_business_rules] WITH (HOLDLOCK) AS target
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
INSERT INTO "opsgroups"."grouptour_task_business_rules"
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

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SummaryStatsSql(OpsGroupsDbSettings db)`  
**Called from:** `SummaryStatsEndpoint.HandleAsync`  
**Parameters:** `@TenantId, @DateFrom, @DateTo, @Today, @WeekEnd, @TodayUtc`

> **Flag #2:** `readiness_avg_pct` is always returned as `0` by the endpoint — the
> `SummaryStatsEndpoint.HandleAsync` has a placeholder `double readinessAvg = 0`. No
> additional SQL is needed to fix this; it requires application logic only.

```sql
SELECT
    (SELECT COUNT(*) FROM opsgroups.grouptour_departures d
     WHERE d.tenant_id = @TenantId AND d.frz_ind = false
     AND d.departure_date >= @DateFrom AND d.departure_date <= @DateTo) AS TotalDepartures,

    (SELECT COUNT(*) FROM opsgroups.grouptour_departure_group_tasks t
     WHERE t.tenant_id = @TenantId AND t.frz_ind = false
     AND t.status = 'overdue'
     AND t.departure_id IN (
         SELECT d2.departure_id FROM opsgroups.grouptour_departures d2
         WHERE d2.tenant_id = @TenantId AND d2.frz_ind = false
         AND d2.departure_date >= @DateFrom AND d2.departure_date <= @DateTo
     )) AS OverdueGroupTasks,

    (SELECT COUNT(*) FROM opsgroups.grouptour_departure_group_tasks t
     WHERE t.tenant_id = @TenantId AND t.frz_ind = false
     AND t.due_date >= @Today AND t.due_date <= @WeekEnd
     AND t.status NOT IN ('complete', 'not_applicable')
     AND t.departure_id IN (
         SELECT d2.departure_id FROM opsgroups.grouptour_departures d2
         WHERE d2.tenant_id = @TenantId AND d2.frz_ind = false
         AND d2.departure_date >= @DateFrom AND d2.departure_date <= @DateTo
     )) AS DueThisWeek,

    (SELECT COUNT(*) FROM opsgroups.grouptour_departure_group_tasks t
     WHERE t.tenant_id = @TenantId AND t.frz_ind = false
     AND t.status = 'complete'
     AND t.updated_on >= @TodayUtc
     AND t.departure_id IN (
         SELECT d2.departure_id FROM opsgroups.grouptour_departures d2
         WHERE d2.tenant_id = @TenantId AND d2.frz_ind = false
         AND d2.departure_date >= @DateFrom AND d2.departure_date <= @DateTo
     )) AS CompletedToday
```

---

## 20. Facets — DISTINCT filter values

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
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
FROM opsgroups.grouptour_departures d
WHERE {same dynamic WHERE as DeparturesListSql}
ORDER BY d.branch_code, d.series_code
```

---

## 21. Series aggregate — GROUP BY series

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `SeriesAggregateSql(OpsGroupsDbSettings db, DepartureFilters f)`  
**Called from:** `DashboardEndpoints.HandleSeriesAggregateAsync`

```sql
SELECT  d.series_code,
        d.series_name,
        SUM(d.pax_count)       AS TotalPax,
        COUNT(d.departure_id)  AS TotalDepartures
FROM    opsgroups.grouptour_departures d
WHERE   {same dynamic WHERE as DeparturesListSql}
GROUP BY d.series_code, d.series_name
ORDER BY d.series_code
```

---

## 22. Heatmap — DISTINCT departure dates (capped at 28)

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `HeatmapDatesSql(OpsGroupsDbSettings db, DepartureFilters f)`  
**Called from:** `DashboardEndpoints.HandleHeatmapAsync`

`dialect.OffsetFetchClause(0, 28)` caps the date window at 28 dates.
`@WindowStart` is added when `DepartureFilters.WindowStart` is not null.

```sql
SELECT DISTINCT d.departure_date
FROM    opsgroups.grouptour_departures d
WHERE   d.tenant_id = @TenantId
AND     d.frz_ind   = false
[AND    d.departure_date >= @DateFrom]
[AND    d.departure_date <= @DateTo]
[AND    d.branch_code = @BranchCode]
[AND    d.series_code = @SeriesCode]
[AND    d.destination_code = @DestinationCode]
[AND    d.ops_manager_initials = @OpsManager]
[AND    d.ops_exec_initials = @OpsExec]
[AND    d.departure_date >= @WindowStart]          -- added when WindowStart is set
ORDER BY d.departure_date
OFFSET 0 ROWS FETCH NEXT 28 ROWS ONLY             -- MSSQL/Postgres; MariaDB: LIMIT 28
```

---

## 23. Cross-service: task template codes (queries PresetsDb)

**File:** `Endpoints/OpsGroupsDbHelper.cs`  
**Method:** `TaskTemplatesForTenantSql(DbType dbType)`  
**Called from:** `SlaHierarchyEndpoint.HandleCodesAvailableAsync`  
**Connection:** Uses `PresetsDbSettings` (separate DB connection — not OpsGroupsDb)  
**Target table:** `presets.group_tasks` — created by Nova.Presets.Api V004 migration

```sql
SELECT  code,
        name,
        source
FROM    presets.group_tasks
WHERE   tenant_id = @TenantId
AND     frz_ind   = false
ORDER BY code
```
