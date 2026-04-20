# Nova.OpsGroups.Api — Build Progress Tracker

**Service:** `Nova.OpsGroups.Api` · **Port:** `5106` · **All paths under** `/api/v1/grouptour-task-*`  
**Phases:** a (Team Members) → c (Activity Templates) → d (Business Rules) → e (Dashboard)  
**Total endpoints:** 12 · **New endpoints:** 2 (Business Rules, Phase d)  
**Phase b (Series / Tours) — REMOVED** per UX handoff `docs/backend-handoff-remove-series-endpoints.md` (2026-04-18). Endpoints never coded. `grouptour_task_series` table is retained — still referenced by departures and SLA rules.

> **How to use:** Mark items `[x]` as completed. If work is paused, this file is the re-entry point.
> Read the Engineering Rules and Design Decisions sections before touching any phase.

---

## Engineering Rules (read before writing a single line of code)

### R1 — Wire format
All JSON bodies (request and response) use **snake_case**. The frontend converts automatically. Never expose camelCase on the wire.

### R2 — HTTP method convention
- **Reads** → `POST` with a body (never bare `GET`), so auto-injected context can be sent.
- **Writes** → `PATCH`.
- **Import** endpoints (`-import`) → `POST`.

### R3 — Auto-injected context (7 fields on every request)
Every request body carries these fields, injected automatically by the frontend. The backend must read them for tenant scoping and audit. **JWT claims win on any conflict with body values.**

| Field | Use |
|---|---|
| `tenant_id` | Row scope (part of tenant PK) |
| `company_code` | Row scope |
| `branch_code` | Row scope |
| `user_id` | Stored as `updated_by` on all saves |
| `browser_locale` | Audit log |
| `browser_timezone` | Audit log |
| `ip_address` | Audit log — accept `X-Forwarded-For` as fallback |

### R4 — Save-payload convention (PATCH requests that use the `changes` envelope)
PATCH bodies send **only changed fields**, each with an `old` and a `new` value. The server must verify each `old` matches the current persisted value before applying `new`. If `old` does not match → **409 Conflict** with pointer `/changes/<field>/old`.

```json
{
  "changes": {
    "field_name": { "old": "<current>", "new": "<desired>" }
  },
  "record_updated_at": "2026-04-18T09:00:00Z",
  ...context fields...
}
```

> **Applies to:** Activity Templates save (Phase c), Business Rules save (Phase d).  
> **Does NOT apply to:** Update Activity, Bulk Update, SLA Rules — these send full field values, but still require `record_updated_at` for the R5 check.

### R5 — Optimistic concurrency check (ALL PATCH endpoints — no exceptions)

**Every PATCH endpoint must perform a timestamp-based concurrency check before persisting.**

**Flow:**
1. Every POST (fetch) response includes `updated_at` on each mutable record.
2. The client passes that value back as `record_updated_at` in the PATCH request body.
3. Before persisting, the server **re-reads** the record from the DB.
4. If DB `updated_at` **is later than** `record_updated_at` → the record was modified by another user since the client loaded it → **409 Conflict**.
5. If DB `updated_at` ≤ `record_updated_at` → proceed.

**Multi-table joins:** when a fetch aggregates multiple tables, the dev team manually decides which `updated_at` to surface. The convention is `MAX(updated_at)` across the joined tables. The same value and the same comparison logic are used for the concurrency check on the corresponding PATCH. This decision must be recorded in the **Design Decisions** table below before coding the affected endpoint.

**409 Conflict response body (RFC 9457):**
```json
{
  "type": "https://novadhruv.com/problems/conflict",
  "title": "Concurrent edit conflict",
  "status": 409,
  "errors": [
    { "pointer": "/record_updated_at", "detail": "Record has been modified since you last loaded it" }
  ]
}
```

**Summary — which `record_updated_at` maps to which table(s):**

| Endpoint | Source of `updated_at` in fetch response | Multi-table? |
|---|---|---|
| Save Activity Template | `updated_at` on the template row | Single table |
| Update Activity (single) | `updated_at` on the activity row in departure fetch/detail | Single table |
| Bulk Update Activities | `record_updated_at` per item inside `updates[]` | Single table per activity |
| Save SLA Rules | Single top-level `record_updated_at` (decision D-2) | Dev team decision D-3 |
| Save Business Rules | `record_updated_at` in request body | Single table |

### R6 — Error format (RFC 9457 Problem Details)
All error responses use this envelope. Never hand-roll a different shape.

```json
{
  "type": "https://novadhruv.com/problems/<code>",
  "title": "Human-readable title",
  "status": <http-status>,
  "errors": [
    { "pointer": "/path/to/field", "detail": "What went wrong" }
  ]
}
```

| Status | Trigger |
|---|---|
| 401 | Missing or expired JWT |
| 403 | Valid JWT but insufficient permissions |
| 404 | Resource not found (`departure_id`, `activity_id`, `template_code`) |
| 409 | Concurrency conflict (R5) or stale `old` value (R4) |
| 422 | Validation failure — `pointer` identifies the offending field path |
| 429 | Rate limit exceeded — include `Retry-After` header |
| 500 | Unexpected server error |

### R7 — JWT middleware
All routes (except `/health`) require `Authorization: Bearer <token>`. Reject with 401 RFC 9457 body on missing, expired, or invalid token. Extract `tenant_id`, `company_code`, `branch_code`, `user_id` from claims.

### R8 — Rate limiting
All routes: Reads 120/min, Writes 60/min. Return 429 with `Retry-After` header and `retry_after` in the body on breach.

### R9 — Realtime events (Phase e only)
Three SignalR events emitted by this service on mutating calls:

| Event | Trigger | Key payload fields |
|---|---|---|
| `grouptour-task-activity-updated` | Single or bulk activity update | `departure_id`, `activity_id`, `template_code`, `status`, `notes`, `updated_by`, `updated_at`, `tenant_id` |
| `grouptour-task-departure-updated` | Departure-level change | `departure_id`, `fields_changed[]`, `snapshot{}`, `updated_by`, `updated_at`, `tenant_id` |
| `grouptour-task-summary-updated` | Any change shifting KPI counts | `overdue`, `due_later`, `done_today`, `done_past`, `tenant_id` |

All events **must include `tenant_id`** in the payload so clients on a shared hub can discard events from other tenants.

---

## Design Decisions (resolve all before coding begins)

| # | Decision | Options | Resolution | Decided |
|---|---|---|---|---|
| D-1 | Bulk update conflict handling (Phase e.5) | Full transaction rollback vs partial success | **Full rollback** — simpler, auditable | [ ] |
| D-2 | SLA Rules `record_updated_at` structure | Single top-level value vs per-rule | **Single top-level** (max of all rules being saved) | [ ] |
| D-3 | SLA Rules — which `updated_at` to surface in fetch | Which table(s) | `MAX(updated_at)` across all joined rule rows | [ ] |
| D-4 | Business Rules — `updated_at` in default response (no DB row) | `null` vs server `now()` | **Server `now()` at request time** (per build-todo) | [x] |
| D-5 | Team members source | Separate table vs `nova_auth` query | **`nova_auth.user_security_rights`** joined to `tenant_user_profile`; grouped by `user_id`; response uses `roles[]` array (not single `role` string); companion `user_security_role_flag` definition table; no OpsGroups table needed; management deferred — seed DB directly for now | [x] |
| D-6 | Activity generation model | Materialised at departure creation vs dynamically at query time | **To confirm** — materialised required for `activity_id` + concurrency tracking | [ ] |
| D-7 | `readiness_pct` formula | % of required complete vs % of all complete | Driven by `readiness_method` Business Rule (`required_only` / `all_activities`) | [x] |
| D-8 | `risk_level` formula | From Business Rules `risk_*_threshold` fields | Computed from Business Rules after Phase d is live | [x] |
| D-9 | `page_size` — constrained or free? | Free positive int vs enum (100/200/500) | **To confirm** — recommend enum to prevent runaway queries | [ ] |
| D-10 | ~~Destination/series import — upsert or insert-only?~~ | ~~Upsert on conflict vs skip duplicate~~ | **N/A — Phase b removed** | [x] |
| D-11 | SignalR hub path and JWT auth wiring | `/hubs/opsgroups` with `OnMessageReceived` JWT hook | **SignalR** — native .NET 10; JWT re-used via same secret | [ ] |

> Fill in the **Decided** column with `[x]` and update the **Resolution** cell as decisions are confirmed.

---

## Proposed DB Schema (confirm before writing migrations)

All OpsGroups tables: `id uuid` PK (app-generated via `Guid.CreateVersion7()`), snake_case columns, scoped by `(tenant_id, company_code, branch_code)`, standard audit columns (`frz_ind`, `created_on`, `created_by`, `updated_on`, `updated_by`, `updated_at` as `timestamptz`/`datetime2`).

### nova_auth DB — new tables (CommonUX.Api migration, not OpsGroups)

These tables are created via a new CommonUX.Api migration. OpsGroups only reads them. They must exist and be seeded before Phase a can be tested.

**`user_security_rights`**

| Column | Type | Notes |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | varchar(10) | |
| `user_id` | varchar(10) | logical ref → `tenant_user_profile` |
| `role_code` | varchar(10) | e.g. `OPSMGR`, `OPSEXEC` |
| `role_flags` | varchar(16) | positional Y/N string; positions defined in companion table |
| `company_code` | varchar(10) | `XXXX` = all companies |
| `branch_code` | varchar(10) | `XXXX` = all branches |
| `frz_ind` + audit columns | | standard |

Unique constraint: `uq_user_security_rights` on `(tenant_id, user_id, role_code, company_code, branch_code)`

**`user_security_role_flag`** (companion — defines flag positions per role_code)

| Column | Type | Notes |
|---|---|---|
| `role_code` | varchar(10) | e.g. `OPSMGR` |
| `flag_position` | int | 1-based |
| `flag_name` | varchar(50) | e.g. `is_ops_manager`, `can_change_due_date` |
| `flag_notes` | varchar(200) | human description |

PK: `(role_code, flag_position)`

> When adding a new flag position: INSERT a row here; document the default value for existing rows in `flag_notes`. No column migrations needed.

**Phase a query pattern** (single SQL call — cross-DB/cross-schema join):
```sql
SELECT  r.user_id, p.display_name, r.role_code, r.role_flags
FROM    user_security_rights r
JOIN    tenant_user_profile p ON p.tenant_id = r.tenant_id AND p.user_id = r.user_id
WHERE   r.tenant_id    = @tenant_id
AND     r.company_code IN (@company_code, 'XXXX')
AND     r.branch_code  IN (@branch_code,  'XXXX')
AND     r.role_code    IN ('OPSMGR', 'OPSEXEC')
AND     r.frz_ind      = 0
AND     p.frz_ind      = 0
```
C# layer groups rows by `user_id`, collects `role_code` values into `roles[]`, computes `initials` from `display_name`.

---

### OpsGroups DB — tables (OpsGroups migrations)

| Table | Unique constraint | Notes |
|---|---|---|
| `grouptour_task_series` | `uq_grouptour_task_series_code` on `(tenant_id, company_code, branch_code, series_code)` | **Retained** — referenced by departures + SLA rules. No fetch/import endpoints. No OpsGroups migration needed (table pre-exists or seeded externally). |
| `grouptour_task_activity_template` | `uq_grouptour_task_activity_template_code` on `(…, template_code)` | Phase c — **first OpsGroups migration** |
| `grouptour_task_departure` | `uq_grouptour_task_departure_code` on `(…, departure_code)` | Phase e |
| `grouptour_task_departure_activity` | `ix_grouptour_task_departure_activity_dep_id` on `departure_id` | Phase e — materialised per activity |
| `grouptour_task_sla_rule` | `uq_grouptour_task_sla_rule` on `(…, level, activity_code, series_code)` | Phase e |
| `grouptour_task_business_rules` | PK only: `(tenant_id, company_code, branch_code)` — one row per tenant scope | Phase d |

---

## Pre-flight Tasks (fix contract docs before coding)

These are Postman / `api-specification.md` corrections. Complete all before writing any endpoint code.

- [ ] **PF-1** Add **409 Conflict** response example to "Save Business Rules" in `novadhruv/postman/postman-Nova.OpsGroups.Api.json`
- [ ] **PF-2** Add `updated_at` field to each template object in the **Fetch Activity Templates** response (spec + Postman) — source for `record_updated_at` on the PATCH
- [ ] **PF-3** Add `record_updated_at` to the **Save Activity Template** PATCH request body (spec + Postman)
- [ ] **PF-4** Add `record_updated_at` to the **Update Activity** PATCH request body (spec + Postman)
- [ ] **PF-5** Add `record_updated_at` **per item** inside `updates[]` for **Bulk Update Activities** (spec + Postman)
- [ ] **PF-6** Add `updated_at` per rule to the **Fetch SLA Rules** response (spec + Postman) — source for Save SLA Rules concurrency check
- [ ] **PF-7** Add `record_updated_at` (single top-level per D-2) to the **Save SLA Rules** PATCH request body (spec + Postman)
- [ ] **PF-8** Add `record_updated_at` to the **Save Business Rules** PATCH request body in `api-specification.md`
- [ ] **PF-9** Add both **Fetch Business Rules** and **Save Business Rules** request examples to `novadhruv/postman/postman-Nova.OpsGroups.Api.json` (endpoints are new — not in either existing collection)
- [ ] **PF-10** Reconcile `lovable/nova-dhruv-ux/docs/postman-Nova.OpsGroups.Api.json` (12 endpoints, UX-amended) with `novadhruv/postman/postman-Nova.OpsGroups.Api.json` (14 endpoints) — copy the UX amendments into the backend collection, add the 2 Business Rules endpoints
- [ ] **PF-11** Update "Fetch Team Members" response example in `api-specification.md` and Postman: change `role` (string) to `roles` (array) to reflect multi-role support

---

## Service Scaffold (one-time — complete before Phase a)

- [x] **S-1** Create `src/services/Nova.OpsGroups.Api/` project structure mirroring existing services
- [x] **S-2** Add project to `novadhruv.slnx`
- [x] **S-3** Add project references: `Nova.Shared`, `Nova.Shared.Web`
- [x] **S-4** Create `appsettings.json` and `opsettings.json` (no secrets in either)
- [x] **S-5** Wire `Program.cs` — 13-step sequence matching Presets.Api pattern (no migrations or email for Phase a)
- [x] **S-6** Register `DefaultTypeMap.MatchNamesWithUnderscores = true`, `DateOnly` and `DateTimeOffset` type handlers
- [x] **S-7** Add to Aspire AppHost (`Nova.AppHost`) — `builder.AddProject<Projects.Nova_OpsGroups_Api>("opsgroups")`; Seq wired; no Redis for Phase a
- [ ] **S-8** Create `Migrations/` folders — deferred to Phase c (first OpsGroups migration; Phase b was removed)
- [ ] **S-9** Verify `/health` returns 200 and service starts clean

---

## Phase a — Team Members

**Endpoints:** 1  
**Frontend:** `src/pages/TeamMembersPage.tsx`  
**Live service fn:** `fetchTeamMembersLive` in `grouptourTask.live.ts`

**Prerequisite:** `user_security_rights` and `user_security_role_flag` tables must exist in `nova_auth` and be seeded with `OPSMGR` / `OPSEXEC` rows before this endpoint can be tested. This is a **CommonUX.Api migration** — not an OpsGroups migration. No OpsGroups DB migration is needed for Phase a.

**Contract:**
- `POST /api/v1/grouptour-task-team-members`
- Request: standard RequestContext (7 fields) only
- Response: `{ "team_members": [{ "user_id", "name", "initials", "roles": ["OPSMGR", "OPSEXEC"] }] }` — one object per user, `roles[]` array (a user can hold multiple roles)
- Source: `nova_auth.user_security_rights` joined to `nova_auth.tenant_user_profile`; `company_code/branch_code = 'XXXX'` wildcard respected in query
- `initials` computed from `display_name` in C# — not stored
- No `updated_at` — read-only reference list; no corresponding PATCH

> **PF task:** The existing Postman and `api-specification.md` show `role` as a single string. Update both to `roles[]` array before coding (add as PF-11 below).

**Tasks:**
- [x] **a-1** Write CommonUX.Api migration V005 (`V005__AddUserSecurityRights.sql`, all 3 dialects)
- [x] **a-2** Seed data written: `planning/seed-data/opsgroups-security-seed-data.sql` — OPSMGR/OPSEXEC flag definitions + ISG user with both roles
- [x] **a-3** Endpoint implemented: `Endpoints/TeamMembers/TeamMembersEndpoint.cs`; SQL in `OpsGroupsDbHelper.TeamMembersQuerySql`
- [x] **a-4** CommonUX.Api V005 migration run; seed data applied
- [x] **a-5** Service started; `/health` → 200 confirmed
- [x] **a-6** Postman: "Fetch Team Members" → 200 with `roles[]`; 401 verified

---

## ~~Phase b — Series / Tours~~ REMOVED

> **Removed 2026-04-18** per UX handoff `docs/backend-handoff-remove-series-endpoints.md`.  
> The frontend page (`SeriesToursPage.tsx`) and all client-side API calls have been deleted. No endpoints were ever coded on the backend, so there is nothing to deprecate or delete here.  
> The `grouptour_task_series` table **must be retained** — `series_code`/`series_name` remain on departure and SLA payloads.  
> If a backoffice surface for managing the series master list is required in future, it will be specified as a new endpoint under a Presets or admin service.

---

## Phase c — Activity Templates

**Endpoints:** 2  
**Frontend:** `src/pages/ActivityTemplatesPage.tsx`  
**Live service fns:** `fetchActivityTemplatesLive`, `saveActivityTemplateLive`

> **This phase establishes the R4 and R5 shared helpers. Get them right here — Phases d and e inherit them.**

**Contracts:**
- `POST /api/v1/grouptour-task-activity-templates` → `{ "templates": [{ "template_code", "template_name", "required", "critical", "sla_offset_days", "reference_date", "source", "is_active", "updated_at" }] }` — `updated_at` per template is required (see PF-2)
- `PATCH /api/v1/grouptour-task-activity-templates/{template_code}` → save-payload R4 + concurrency R5; response: full updated template including new `updated_at`

**Shared helpers to create (c-3) — reused in Phases d and e:**
- `SavePayloadHelper` — merges `changes` dict with current DB row; validates each `old` value; returns 409 with `/changes/<field>/old` pointer on mismatch
- `ConcurrencyHelper.CheckTimestamp(dbUpdatedAt, requestRecordUpdatedAt)` — returns 409 RFC 9457 body if DB value is later
- RFC 9457 error formatter — single shared serialiser; no endpoint hand-rolls its own

**Tasks:**
- [ ] **c-1** PF-2 and PF-3 must be done first (add `updated_at` + `record_updated_at` to spec + Postman)
- [ ] **c-2** Write migration: `grouptour_task_activity_template` table (all 3 dialects)
- [ ] **c-3** Implement shared helpers: `SavePayloadHelper`, `ConcurrencyHelper`, RFC 9457 formatter
- [ ] **c-4** Implement `POST /grouptour-task-activity-templates` (include `updated_at` per row)
- [ ] **c-5** Implement `PATCH /grouptour-task-activity-templates/{template_code}` (R4 + R5)
- [ ] **c-6** Test: template grid renders; counts (total, critical, required) match
- [ ] **c-7** Test: edit one cell → PATCH body contains only that field in `changes`; reload → value persists
- [ ] **c-8** Test: submit PATCH with `record_updated_at` older than DB `updated_at` → 409 (`/record_updated_at` pointer)
- [ ] **c-9** Test: submit PATCH with stale `old` value → 409 (`/changes/<field>/old` pointer)
- [ ] **c-10** Test: duplicate `template_code` → 422
- [ ] **c-11** Postman: run "Fetch Activity Templates" and "Save Activity Template"; verify 200, 409, 422, 401

---

## Phase d — Business Rules ★ NEW ENDPOINTS ★

**Endpoints:** 2  
**Frontend:** `src/pages/BusinessRulesPage.tsx`  
**Live service fns:** `fetchBusinessRulesLive`, `saveBusinessRulesLive` (to be added)  
**Spec:** `api-specification.md` lines 2555–2676; `operations-api-build-guide.md` section e

> Highest validation complexity in the service. Do not start until Phase c helpers (c-3) are in place.

**Table: `grouptour_task_business_rules`**  
PK: `(tenant_id, company_code, branch_code)` — one row per tenant scope.

| Column | Type | Default |
|---|---|---|
| `overdue_critical_days` | int | `3` |
| `overdue_warning_days` | int | `7` |
| `readiness_method` | varchar(32) | `required_only` |
| `risk_red_threshold` | varchar(64) | `critical_overdue` |
| `risk_amber_threshold` | varchar(64) | `any_overdue` |
| `risk_green_threshold` | varchar(64) | `no_overdue` |
| `heatmap_red_max` | int | `39` |
| `heatmap_amber_max` | int | `79` |
| `auto_mark_overdue` | boolean NOT NULL DEFAULT true | |
| `include_na_in_readiness` | boolean NOT NULL DEFAULT false | |
| `updated_at` | timestamptz / datetime2 | auto-managed |
| `updated_by` | varchar(16) | from `user_id` claim |

**Contracts:**
- `POST /api/v1/grouptour-task-business-rules` — if no DB row exists → return in-memory defaults; **do not INSERT a row**; set `updated_at` to server `now()`, `updated_by` to requesting `user_id`
- `PATCH /api/v1/grouptour-task-business-rules` — R4 (changes envelope) + R5 (timestamp check); INSERT on first save, UPDATE thereafter; response: full updated `business_rules` block

**Concurrency check order for Save (e-2):**
1. Re-read record (or confirm no row → defaults apply)
2. R5 check: if DB `updated_at` > `record_updated_at` → 409
3. R4 check: per field in `changes`, if stored value ≠ `old` → 409 pointer `/changes/<field>/old`
4. Merge `new` values
5. Run validation matrix
6. Persist (INSERT or UPDATE)

**Validation matrix (applied to merged state before persist):**

| Rule | 422 pointer |
|---|---|
| `overdue_critical_days` ≥ 0 | `/changes/overdue_critical_days/new` |
| `overdue_critical_days` ≤ `overdue_warning_days` | `/changes/overdue_critical_days/new` |
| `overdue_warning_days` ≥ 0 | `/changes/overdue_warning_days/new` |
| `heatmap_red_max` in 0–100 | `/changes/heatmap_red_max/new` |
| `heatmap_amber_max` in 0–100 | `/changes/heatmap_amber_max/new` |
| `heatmap_red_max` < `heatmap_amber_max` | `/changes/heatmap_red_max/new` |
| `readiness_method` ∈ `{required_only, all_activities}` | `/changes/readiness_method/new` |
| `auto_mark_overdue` is boolean | `/changes/auto_mark_overdue/new` |
| `include_na_in_readiness` is boolean | `/changes/include_na_in_readiness/new` |
| Unknown field key in `changes` | `/changes/<unknown>` |

**Tasks:**
- [ ] **d-1** PF-8 and PF-9 must be done first
- [ ] **d-2** Write migration: `grouptour_task_business_rules` table (all 3 dialects)
- [ ] **d-3** Implement `POST /grouptour-task-business-rules` (defaults when no row — no INSERT)
- [ ] **d-4** Implement `PATCH /grouptour-task-business-rules` (R4 + R5 + validation matrix + INSERT-or-UPDATE)
- [ ] **d-5** Test: fetch with no DB row → returns defaults; confirm nothing inserted in DB
- [ ] **d-6** Test: PATCH `overdue_warning_days` 7 → 5 → response shows `5` and fresh `updated_at`
- [ ] **d-7** Test: fetch again → reflects saved values
- [ ] **d-8** Test: PATCH `heatmap_red_max: 90, heatmap_amber_max: 80` → 422 pointer `/changes/heatmap_red_max/new`
- [ ] **d-9** Test: PATCH with `record_updated_at` older than DB `updated_at` → 409
- [ ] **d-10** Test: PATCH with stale `old` value → 409 pointer `/changes/<field>/old`
- [ ] **d-11** Test: PATCH with unknown field key in `changes` → 422 pointer `/changes/<unknown>`
- [ ] **d-12** Test: missing `Authorization` header → 401
- [ ] **d-13** Postman: run "Fetch Business Rules" and "Save Business Rules" including 409 example; verify all response codes

---

## Phase e — Dashboard

**Endpoints:** 7 · **SignalR events:** 3  
**Frontend:** `src/pages/OpsAdminDashboardPage.tsx`  
**Build in sub-order e.1 → e.7** (grid renders before edits or realtime are wired)

---

### e.1 — Summary Stats

- `POST /api/v1/grouptour-task-summary-stats`
- Request: `date_from`, `date_to` + context
- Response: `{ "total_departures", "overdue_activities", "due_this_week", "completed_today", "readiness_avg_pct" }`
- Computed at read time — no `updated_at`, no PATCH
- Must use same filter scope as departures fetch for KPI consistency

- [ ] **e.1-1** Implement endpoint (aggregation over departure + activity tables)
- [ ] **e.1-2** Test: KPI values match counts derived from the full departure grid for the same filter window

---

### e.2 — Fetch Departures (paginated grid)

- `POST /api/v1/grouptour-task-departures`
- Filters: `date_from`, `date_to`, `series_code`, `destination_code`, `ops_manager_user_id`, `ops_exec_user_id`, `search`, `page`, `page_size`
- Response: `{ "departures": [...], "total": N, "page": 1, "page_size": 100 }`
- Each departure embeds `activities[]` — each activity **must include `updated_at`** (source for R5 on e.4 and e.5)
- Each departure includes computed `readiness_pct` and `risk_level`

- [ ] **e.2-1** Confirm D-6 (materialised activities vs dynamic generation) and D-9 (page_size constraint)
- [ ] **e.2-2** Write migrations: `grouptour_task_departure`, `grouptour_task_departure_activity` (all 3 dialects)
- [ ] **e.2-3** Implement endpoint with all filters + pagination
- [ ] **e.2-4** Test: each filter applied individually returns strict subset; `total` decreases
- [ ] **e.2-5** Test: `page=2` returns next slice; `total` unchanged

---

### e.3 — Fetch Departure Detail (drawer)

- `POST /api/v1/grouptour-task-departures/{departure_id}`
- Request: context only
- Response: single departure — same shape as grid row but full activity detail; include `updated_at` per activity
- 404 if `departure_id` not found in tenant scope

- [ ] **e.3-1** Implement endpoint (same query as e.2 but single-row, no pagination)
- [ ] **e.3-2** Test: open drawer → activity list matches embedded summary on the grid row

---

### e.4 — Update Activity (single edit)

PF-4 must be done first.

- `PATCH /api/v1/grouptour-task-departures/{departure_id}/activities/{activity_id}`
- Request: `{ "status", "notes", "completed_date", "record_updated_at", ...context }`
- R5 check: re-read activity row; if DB `updated_at` > `record_updated_at` → 409
- Persist; set `updated_at = now()`, `updated_by = user_id`
- Response: `{ "activity_id", "status", "notes", "completed_date", "updated_at" }`
- **After persist:** emit `grouptour-task-activity-updated`; also emit `grouptour-task-departure-updated` and `grouptour-task-summary-updated` if KPIs shift

- [ ] **e.4-1** Wire SignalR hub here (D-11) — first use in Phase e
- [ ] **e.4-2** Implement endpoint (R5 + persist + 3 events)
- [ ] **e.4-3** Test: edit status → PATCH succeeds → grid cell updates → SignalR event received by other open sessions
- [ ] **e.4-4** Test: PATCH with stale `record_updated_at` → 409

---

### e.5 — Bulk Update Activities

PF-5 must be done first. Decision D-1 must be resolved (full rollback confirmed).

- `PATCH /api/v1/grouptour-task-activity-bulk-update`
- Each item in `updates[]`: `{ "departure_id", "activity_id", "old_status", "new_status", "notes", "record_updated_at" }`
- For each item: `old_status` field check + R5 timestamp check
- On any conflict → **full transaction rollback** (D-1); return error list
- Response on success: `{ "success": true, "updated_count": N, "results": [...] }`
- Emit `grouptour-task-activity-updated` per updated activity; emit `grouptour-task-summary-updated` once at end

- [ ] **e.5-1** Implement endpoint (batch inside single transaction; rollback on any conflict)
- [ ] **e.5-2** Test: select 5 rows, bulk mark `INV` complete → `updated_count: 5`
- [ ] **e.5-3** Test: undo within 5 s — send revert PATCH → all statuses restored
- [ ] **e.5-4** Test: one item with stale `record_updated_at` in a 5-item batch → full rollback; 0 items persisted

---

### e.6 — Fetch SLA Rules

PF-6 must be done first. D-3 must be resolved before coding.

- `POST /api/v1/grouptour-task-sla-rules`
- Request: context only
- Response: `{ "global": [...], "series_overrides": [...] }` with `updated_at` per rule
- `updated_at` per rule is the source for R5 on Save SLA Rules

- [ ] **e.6-1** Write migration: `grouptour_task_sla_rule` table (all 3 dialects)
- [ ] **e.6-2** Implement endpoint (two result sets: global rules + series overrides)
- [ ] **e.6-3** Test: SLA editor loads; global and series-level rules visible in hierarchy

---

### e.7 — Save SLA Rules

PF-7 must be done first. D-2 and D-3 must be resolved.

- `PATCH /api/v1/grouptour-task-sla-rules`
- Request: `{ "rules": [...], "record_updated_at": "...", ...context }` — single top-level `record_updated_at` (D-2)
- R5 check: re-read affected rules; if any DB `updated_at` > `record_updated_at` → 409
- Upsert at series tier only; do not overwrite global rules via this endpoint
- Departures recompute due dates **lazily** on next fetch (no eager recompute)
- Response: `{ "success": true, "saved_count": N, "rules": [...] }` with `updated_at` per saved rule

- [ ] **e.7-1** Implement endpoint (upsert series-level rules; R5 on single top-level timestamp)
- [ ] **e.7-2** Test: override one offset at series level → save → fetch → override visible at series tier
- [ ] **e.7-3** Test: affected departure re-fetched shows updated due date for that series
- [ ] **e.7-4** Test: PATCH with stale `record_updated_at` → 409

---

## Cross-cutting Checklist (complete before marking any phase fully done)

- [ ] **CC-1** JWT middleware applied to all routes; 401 RFC 9457 on failure
- [ ] **CC-2** `ConcurrencyHelper.CheckTimestamp` (R5) implemented as a shared function; all five PATCH endpoints use it
- [ ] **CC-3** `SavePayloadHelper` old-value check (R4) implemented as shared function; Activity Templates + Business Rules use it
- [ ] **CC-4** RFC 9457 error formatter is a single shared serialiser; no endpoint hand-rolls its own error body
- [ ] **CC-5** Rate-limit middleware returns 429 with `Retry-After` on all routes; Reads 120/min, Writes 60/min
- [ ] **CC-6** All `updated_at` columns are `timestamptz`/`datetime2`; all timestamps stored and returned in UTC ISO 8601
- [ ] **CC-7** All queries scoped by `(tenant_id, company_code, branch_code)` — no cross-tenant leakage possible
- [ ] **CC-8** SignalR events include `tenant_id` in every payload

---

## Open Decisions Log (fill in during build)

| # | Question | Decision | Decided by | Date |
|---|---|---|---|---|
| D-1 | Bulk update (e.5): partial success or full rollback? | Full rollback | | |
| D-2 | SLA Rules (e.7): one top-level `record_updated_at` or per-rule? | Single top-level | | |
| D-3 | SLA Rules (e.6/e.7): which `updated_at` to surface on multi-table join? | `MAX(updated_at)` | | |
| D-5 | Team members: `nova_auth` role filter or separate table? | `nova_auth.user_security_rights` + `user_security_role_flag`; `roles[]` in response; seed DB directly for now | rajeev | 2026-04-18 |
| D-6 | Activity generation: materialised at departure creation or dynamic? | | | |
| D-9 | `page_size`: enum (100/200/500) or free? | | | |
| D-11 | SignalR hub path and JWT wiring | `/hubs/opsgroups` + `OnMessageReceived` | | |

---

## Progress Summary

| Phase | Status | Notes |
|---|---|---|
| Pre-flight (PF) | Partial | PF-11 done (roles[] contract); PF-1..10 deferred to their phase |
| Scaffold (S) | **Complete** | S-1..S-9 done; AppHost + slnx updated |
| a — Team Members | **Complete** | Tested 2026-04-18; migration run, seed applied, Postman verified |
| ~~b — Series / Tours~~ | **REMOVED** | Removed per UX handoff 2026-04-18; endpoints never coded; series table retained |
| c — Activity Templates | Not started | **Next phase** — establishes R4 + R5 shared helpers |
| d — Business Rules | Not started | |
| e.1 — Summary Stats | Not started | |
| e.2 — Fetch Departures | Not started | |
| e.3 — Departure Detail | Not started | |
| e.4 — Update Activity | Not started | |
| e.5 — Bulk Update | Not started | |
| e.6 — Fetch SLA Rules | Not started | |
| e.7 — Save SLA Rules | Not started | |
| Cross-cutting (CC) | Not started | |
