# Nova.OpsGroups.Api — Developer Guide

**Port:** 5106  
**Postman collection:** `postman/postman-Nova.OpsGroups.Api.json`  
**SQL review:** `src/services/Nova.OpsGroups.Api/docs/opsgroups-sql-review.md`

---

## What This Service Is

`Nova.OpsGroups.Api` owns group tour departure task tracking and SLA management:

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/grouptour-task-hello` | Health/smoke test — returns `{ "message": "Hello from Nova.OpsGroups.Api" }` |
| `POST /api/v1/grouptour-task-departures` | Paginated departure list with group tasks and readiness (max 500 per page) |
| `POST /api/v1/grouptour-task-departures/{departure_id}` | Single departure detail with all group tasks |
| `PATCH /api/v1/grouptour-task-departures/{departure_id}/group-tasks/{group_task_id}` | Update a single group task (status, notes, completed_date) |
| `PATCH /api/v1/group-task-bulk-update-group-tasks` | Bulk update group tasks with optimistic concurrency (atomic — rolls back on any conflict) |
| `POST /api/v1/grouptour-task-sla-rules` | Fetch all SLA rules for tenant |
| `PATCH /api/v1/grouptour-task-sla-rules` | Save (upsert) one SLA rule |
| `POST /api/v1/group-task-sla-hierarchy` | Full SLA rule tree (global + series levels) built in C# from DB rows |
| `PATCH /api/v1/group-task-sla-rule-save` | Save one SLA change with optimistic lock and audit trail |
| `POST /api/v1/group-task-sla-audit` | Paginated audit log for a scope key |
| `POST /api/v1/group-task-codes-available` | Available task codes from Presets DB for use in SLA rule building |
| `POST /api/v1/grouptour-task-summary-stats` | Dashboard KPI counts (total departures, overdue, due this week, completed today) |
| `POST /api/v1/grouptour-task-departures-summary` | Dashboard departure cards with per-departure readiness and risk level |
| `POST /api/v1/grouptour-task-departures-facets` | Distinct filter values for branch, ops manager, ops exec, series |
| `POST /api/v1/grouptour-task-departures-tasks` | Task-centric paginated view across all departures |
| `POST /api/v1/grouptour-task-series-aggregate` | Per-series pax count and departure count rollup |
| `POST /api/v1/grouptour-task-heatmap` | Up to 28 distinct departure dates for a heatmap calendar view |
| `POST /api/v1/group-task-business-rules` | Fetch business rules (overdue thresholds, readiness method, risk bands) |
| `PATCH /api/v1/group-task-business-rules` | Save (upsert) business rules for a tenant/company/branch |

Reads from three databases:
- **AuthDb** (`nova_auth` schema) — user display names and role assignments, managed by `Nova.CommonUX.Api`
- **OpsGroupsDb** (`opsgroups` schema) — departures, group tasks, SLA rules, business rules
- **PresetsDb** (`presets` schema) — task template codes, managed by `Nova.Presets.Api`

---

## Configuration Files

| File | Purpose | Reload |
|---|---|---|
| `appsettings.json` | Encrypted connection strings, JWT, API keys | Restart required |
| `opsettings.json` | Logging, rate limiting, SQL logging | Hot-reload via `IOptionsMonitor` |

### appsettings.json — required sections

```jsonc
{
  "AuthDb":       { "ConnectionString": "<encrypted>", "DbType": "MsSql" },
  "OpsGroupsDb":  { "ConnectionString": "<encrypted>", "DbType": "MsSql" },
  "PresetsDb":    { "ConnectionString": "<encrypted>", "DbType": "MsSql" },
  "AppBaseUrl":   "http://localhost:3000",
  "Jwt": {
    "Issuer":    "https://auth.nova.internal",
    "Audience":  "nova-api",
    "SecretKey": "<encrypted>"
  },
  "InternalAuth": {
    "ServiceName":          "nova-opsgroups",
    "SecretKey":            "<encrypted>",
    "TokenLifetimeSeconds": 300
  }
}
```

All three `DbType` values must match the actual target database — `MsSql`, `Postgres`, or `MariaDb`.

---

## Database Migrations

Migrations run manually via the unversioned admin endpoint:

```
POST http://localhost:5106/run-opsgroups-migrations
```

Migrations live in:
```
src/services/Nova.OpsGroups.Api/Migrations/
  MsSql/    V001__CreateOpsGroups.sql
  Postgres/ V001__CreateOpsGroups.sql
  MariaDb/  V001__CreateOpsGroups.sql
```

| Migration | Tables added |
|---|---|
| V001 | `grouptour_departures`, `grouptour_departure_group_tasks`, `grouptour_sla_rules`, `grouptour_sla_rule_audit`, `grouptour_task_business_rules` |

> **Pending — V002 migration needed:** The `grouptour_departure_group_tasks` table is
> missing a `required boolean` column that the code already queries. A V002 migration
> `ALTER TABLE grouptour_departure_group_tasks ADD required boolean NOT NULL DEFAULT false`
> is required for all three dialects before the departure endpoints will work.
> See flag #5 in `opsgroups-sql-review.md`.

---

## Removed / Relocated Endpoints — 410 Gone

These routes return `410 Gone` with a `detail` message explaining where to find the replacement:

| Route (410) | Detail |
|---|---|
| `POST /api/v1/grouptour-task-team-members` | Moved to Nova.Presets.Api: `POST /api/v1/users/by-role` |
| `POST /api/v1/grouptour-task-tour-generics` | Moved to Nova.Presets.Api: `POST /api/v1/groups/tour-generics` |
| `POST /api/v1/group-task-tour-generics-search` | Moved to Nova.Presets.Api: `POST /api/v1/groups/tour-generics/search` |
| `POST /api/v1/grouptour-task-series` | Endpoint removed |
| `POST /api/v1/grouptour-task-series-import` | Endpoint removed |

---

## Departure List — Request Shape

`POST /api/v1/grouptour-task-departures`

All filter fields are optional. `RequestContext` standard fields (`tenant_id`, `company_code`,
`branch_code`, `user_id`, etc.) are always required.

| Field | Type | Notes |
|---|---|---|
| `date_from` | `date` | `yyyy-MM-dd` — filter departure_date ≥ |
| `date_to` | `date` | `yyyy-MM-dd` — filter departure_date ≤ |
| `branch_code` | `string` | exact match |
| `series_code` | `string` | exact match |
| `tour_generic_code` | `string` | passed through to filter record but not wired to a SQL clause (see flag #6 in SQL review) |
| `destination_code` | `string` | exact match |
| `ops_manager` | `string` | matches `ops_manager_initials` |
| `ops_exec` | `string` | matches `ops_exec_initials` |
| `search` | `string` | LIKE against `series_name`, `destination_name`, `departure_id` — passed as `%value%` |
| `ignore_complete` | `bool` | if true, excludes departures where all tasks are complete or not_applicable |
| `projection` | `string` | `"full"` (default), `"tasks"`, `"calendar"` — controls response shape |
| `page` | `int` | 1-based, defaults to 1 |
| `page_size` | `int` | defaults to 100, max 500 |

**Response:**
```json
{
  "departures": [ ... ],
  "total":     42,
  "page":      1,
  "page_size": 100
}
```

---

## Departure Response Shapes

Three projections are available via the `projection` field:

**`"full"` / `"tasks"` (default)** — includes `group_tasks[]` array:
```json
{
  "departure_id":     "BHU2026-03",
  "series_code":      "BHU",
  "series_name":      "Bhutan Spring",
  "departure_date":   "2026-03-15",
  "return_date":      "2026-03-28",
  "destination_code": "BHU",
  "destination_name": "Bhutan",
  "branch_code":      "LON",
  "pax_count":        18,
  "booking_count":    12,
  "ops_manager":      "Alice Smith",
  "ops_exec":         "Bob Jones",
  "gtd":              true,
  "notes":            null,
  "group_tasks": [
    {
      "group_task_id":  "BHU2026-03-PRE_DOCS",
      "template_code":  "PRE_DOCS",
      "status":         "complete",
      "due_date":       "2026-03-08",
      "completed_date": "2026-03-07",
      "notes":          null,
      "source":         "GLOBAL"
    }
  ],
  "readiness_pct": 80,
  "risk_level":    "amber"
}
```

**`"calendar"`** — minimal shape for calendar views:
```json
{
  "departure_id":     "BHU2026-03",
  "departure_date":   "2026-03-15",
  "destination_name": "Bhutan",
  "readiness_pct":    80,
  "risk_level":       "amber"
}
```

---

## Group Task Statuses

Valid `status` values for group tasks:

| Value | Meaning |
|---|---|
| `not_started` | Default — task has not been acted on |
| `in_progress` | Work has begun |
| `complete` | Task is done |
| `not_applicable` | Task does not apply to this departure |
| `overdue` | Past due date and not complete (set by `auto_mark_overdue` business rule) |

---

## Bulk Update — Optimistic Concurrency

`PATCH /api/v1/group-task-bulk-update-group-tasks`

Each item in the `updates[]` array must include `old_status`. Before applying each update,
the current status is fetched and compared. If any item has a conflict (current ≠ old_status),
the entire transaction is **rolled back** and 409 is returned with a `results` extension:

```json
{
  "title": "Conflict",
  "detail": "One or more tasks had a status conflict. No changes were saved.",
  "status": 409,
  "results": [
    { "group_task_id": "...", "success": false, "new_status": "complete", "error": "optimistic_conflict" }
  ]
}
```

Request body:
```json
{
  "tenant_id":    "BTDK",
  "company_code": "MAIN",
  "branch_code":  "LON",
  "user_id":      "USR001",
  "updates": [
    {
      "departure_id":   "BHU2026-03",
      "group_task_id":  "BHU2026-03-PRE_DOCS",
      "old_status":     "not_started",
      "new_status":     "complete",
      "notes":          null
    }
  ]
}
```

Note: `completed_date` is always set to `null` in bulk update (not yet exposed as a field).

---

## SLA Hierarchy

The SLA system has two levels:

| Level | `scope_key` format | Meaning |
|---|---|---|
| `global` | `"global"` | Default offset for all tenants |
| `tour_series` | series code (e.g. `"BHU"`) | Override for a specific tour series |

`POST /api/v1/group-task-sla-hierarchy` fetches all rules and builds the full tree in C#,
returning global and series-level entries side by side.

`PATCH /api/v1/group-task-sla-rule-save` saves individual changes with optimistic locking
(via a `version` token). Each save also writes an audit row to `grouptour_sla_rule_audit`.

Setting `new_value: null` at `tour_series` level deletes the row (inherits from global);
setting `null` at `global` level is a no-op.

---

## Business Rules — Fields

`POST /api/v1/group-task-business-rules` (fetch) / `PATCH` (save)

Scoped to `(tenant_id, company_code, branch_code)`. Returns defaults if no row exists.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `overdue_critical_days` | `int` | `3` | Days past due before a task is critical-overdue |
| `overdue_warning_days` | `int` | `7` | Days past due before warning |
| `readiness_method` | `string` | `"required_only"` | `"required_only"` or `"all_tasks"` |
| `risk_red_threshold` | `string` | `"critical_overdue"` | Threshold expression for red risk |
| `risk_amber_threshold` | `string` | `"any_overdue"` | Threshold expression for amber risk |
| `risk_green_threshold` | `string` | `"no_overdue"` | Threshold expression for green risk |
| `heatmap_red_max` | `int` | `39` | Readiness % upper bound for red on heatmap |
| `heatmap_amber_max` | `int` | `79` | Readiness % upper bound for amber on heatmap |
| `auto_mark_overdue` | `bool` | `true` | Auto-set status to `overdue` when past due_date |
| `include_na_in_readiness` | `bool` | `false` | Count `not_applicable` tasks as complete in readiness calc |

---

## Known Incomplete Items

| Item | Location | Notes |
|---|---|---|
| `readiness_avg_pct` always returns `0` | `SummaryStatsEndpoint.HandleAsync` | Placeholder — logic not yet implemented |
| `ComputeReadinessPct` ignores `readiness_method` and `include_na_in_readiness` | `OpsGroupsDbHelper.ComputeReadinessPct` | TODOs in code — uses simple `complete + not_applicable / total` fallback |
| Business rules not fetched for departure list readiness | `DeparturesEndpoint.HandleListAsync` | TODO comment — `ComputeReadinessPct` called without rules; uses defaults |
| `tour_generic_code` filter not wired to SQL | `DeparturesListSql` / `DepartureFilters` | Field is in DepartureFilters record but has no SQL clause |
| `grouptour_departure_group_tasks.required` column missing from V001 migrations | All three dialects | V002 migration needed — see SQL review flag #5 |
