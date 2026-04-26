# Nova Platform — Deferred Work Register

Items that are deliberately incomplete. Each entry states what is missing, where to find it in the code, and why it was deferred rather than done at the time.

Update this file when an item is completed (mark ~~done~~ with date) or when a new deferral is agreed. Do not delete completed entries — the audit trail matters.

---

## Nova.CommonUX.Api

### Social login — Microsoft and Apple token verifiers
**Status:** Not started  
**Files:** `src/services/Nova.CommonUX.Api/Services/SocialTokenVerifier.cs` lines 61, 69  
**What:** The initiate endpoints for Microsoft and Apple social login exist and return the correct redirect URL. The verify (callback) endpoints exist but call stub verifiers that always throw. Real verification needs implementing:
- Microsoft: use `Microsoft.Identity.Client` (MSAL) to validate the id_token
- Apple: fetch Apple's public keys from `https://appleid.apple.com/auth/keys` and verify the JWT

**Why deferred:** Auth flows require real OAuth app credentials configured in a test environment. Deferred until those are provisioned.

---

### ~~Race condition — token double-use + failed login counter — DONE (2026-04-18)~~
Already implemented during CommonUX manual testing:
- `ResetPasswordEndpoint` + `MagicLinkVerifyEndpoint`: atomic `UPDATE ... WHERE id = @Id AND used_on IS NULL`; `if (tokenUsed == 0)` → 401.
- `LoginEndpoint`: `failed_login_count = failed_login_count + 1` in-DB increment; `CASE WHEN` in the same statement sets `locked_until` when threshold is reached — no read-then-write.

---

## Nova.ToDo.Api

### Rights checks — all endpoints
**Status:** Not started  
**Files:** All endpoint files in `src/services/Nova.ToDo.Api/Endpoints/` — every endpoint has a `// TODO: rights check` comment  
**What:** Endpoints currently trust that any authenticated user for the tenant can perform any operation. Need to enforce role-based read/write/delete/freeze/complete rights from `nova_auth.user_security_rights`.  
**Why deferred:** Rights model for ToDo not yet specified. Blocked on product decision about which roles get which permissions.

---

### Multi-dialect support — MSSQL-only SQL
**Status:** Not started  
**Files:** Multiple endpoints in `src/services/Nova.ToDo.Api/Endpoints/`  
**What:** The ToDo service was built against legacy MSSQL (sales97 schema). Several constructs need dialect-safe equivalents before Postgres/MariaDB tenants can use it:

| MSSQL construct | Postgres equivalent | MariaDB equivalent |
|---|---|---|
| `GETUTCDATE()` | `NOW() AT TIME ZONE 'UTC'` | `UTC_TIMESTAMP()` |
| `SELECT TOP 1` | `LIMIT 1` | `LIMIT 1` |
| `SCOPE_IDENTITY()` | `RETURNING id` (in INSERT) | `SELECT LAST_INSERT_ID()` |
| `CONVERT(date, col)` | `CAST(col AS DATE)` | `CAST(col AS DATE)` |
| Cross-DB JOINs to `sales97.*` | Cross-schema (same DB) | Cross-DB (separate DBs) |

**Why deferred:** All current tenants using ToDo are MSSQL. Postgres/MariaDB migration is 2–3 years away per the architecture plan.

---

### SummaryByContextEndpoint — incomplete scope branches
**Status:** Not started  
**File:** `src/services/Nova.ToDo.Api/Endpoints/SummaryByContextEndpoint.cs` lines 124–125, 183  
**What:**
- `account_code_client` scope: should join open enquiries/quotes/bookings within a 15-day return window
- `supplier_code` scope: should filter `DoneOn` within last 30 days
- Inline query scope for NULL context not yet implemented  
**Why deferred:** Business rules for these scopes need product confirmation before coding.

---

### ~~`expected_lock_ver` rename + Pattern A 409 `server_row` — DONE 2026-04-26~~
**What was done:** All five write endpoints (`update`, `delete`, `complete`, `undo-complete`, `freeze`) updated:
- Request record field renamed `LockVer` → `ExpectedLockVer`; JSON key is now `expected_lock_ver`; validation error key updated to match.
- 409 conflict responses now include `extensions: { seq_no, server_lock_ver, server_row }` where `server_row` is the full `ToDoProjections.Project(current)` shape re-read via `ToDoDbHelper.FetchBySeqNoAsync`.
- Update/Delete/UndoComplete: replaced bare EXISTS scalar with `FetchBySeqNoAsync` re-read → null is 404, found is 409 with `server_row`.
- Complete/Freeze: re-read after `affected == 0` for `server_row` (pre-read confirmed existence but cannot be reused after concurrent write).
- `ToDoDbHelper.FetchBySeqNoAsync` added to `Endpoints/ToDoDbHelper.cs` — single source for the full MSSQL-LEGACY SELECT with FORMAT aliases.

---

## Nova.OpsGroups.Api

### ~~SLA hierarchy — UX alignment (endpoint overhaul) DONE 2026-04-26~~
**What was done:**
- `HandleHierarchyAsync` rewritten — accepts `{ tour_generic_code, year_floor }`, fetches GLOB + TG + all series + all departures in one request, returns `{ global: Level, levels: [Level...] }` with `entries[].ref_date` and `cells[code].{ own, resolved, resolved_from }` per the tristate contract (`backend-handoff-sla-tristate.md`).
- `HandleRuleSaveAsync` field renames: `scope.level`, `group_task_code`, `reference_date`, `old`/`new` (was `scope_type`, `task_code`, `enq_event_code`, `old_cell`/`new_cell`). Response now `{ success, version, updated_at, updated_by }`.
- `HandleAuditAsync` accepts `scope_key` string (`global`, `tg_X`, `ts_X`, `dep_<uuid>`). Response fields renamed to `group_task_code`, `reference_date`, `changed_at`.
- Per-cell conflict detection (Phase 1 read + rollback + 409) retained and adapted to new field names.
- Wire ↔ DB mappings: `"departure"↔"DP"`, `"return"↔"RT"`, `"ji_exists"↔"JI"`; `"global"↔"GLOB"` etc.

**Remaining — UX team:** Handle 409 on save — show conflict banner, highlight affected cells, offer Discard / Overwrite.

**`na` terminal semantics — CONFIRMED AND IMPLEMENTED 2026-04-26:**  
UX team confirmed: `na` is strictly terminal. `BuildEntry` now walks outermost → innermost (`Enumerable.Reverse(chain)`); updates the candidate at each found own row; breaks immediately on `na`. A child scope's own `set` cannot override any ancestor's `na`.

**Design note — `base_version` not validated by design:**  
`base_version` is accepted in the `PATCH /api/v1/group-task-sla-rule-save` request but not checked server-side. Per-cell `old`/`new` comparison is the authoritative conflict detection; a `base_version` mismatch with no cell conflicts means an unrelated cell changed — safe to proceed. If UX later requires a hard 409 on any stale `base_version`, add a pre-check before Phase 1 in `HandleRuleSaveAsync` (~8 lines).

---

### ~~`ComputeReadinessPct` — DONE~~
Full implementation in `OpsGroupsDbHelper.cs`: `required_only` (Completed Required / Total Required) and `all_tasks` (Completed All / Total All excl N/A) branches; `includeNaInReadiness` flag counts `not_applicable` as complete in both modes.

---

### ~~BusinessRulesRow wired into readiness calls — DONE~~
`BusinessRulesRow` (`ReadinessMethod`, `IncludeNaInReadiness`) now fetched and passed into `ComputeReadinessPct` in: `DeparturesEndpoint` (list + detail), `DashboardEndpoints` (summary, tasks view, series aggregate, heatmap), and `SummaryStatsEndpoint`.

---

### ~~`readiness_avg_pct` in SummaryStatsEndpoint — DONE~~
`SummaryStatsEndpoint` now fetches departures + tasks for the date window and averages `ComputeReadinessPct` per departure, respecting `ReadinessMethod` and `IncludeNaInReadiness` from `BusinessRulesRow`.

---

### ~~Series endpoints — retire with 410 Gone DONE~~
`src/services/Nova.OpsGroups.Api/Endpoints/RemovedEndpointsEndpoint.cs` — both `/grouptour-task-series` and `/grouptour-task-series-import` already return 410 Gone.

---

### ~~Dashboard filter revamp — new endpoints — DONE 2026-04-26~~
**What was done:**
- Extended `POST /api/v1/grouptour-task-departures`: added `branch_code_filter string[]`, `quick_status` (C# post-filter: overdue/at_risk/ready/due_later/done_today/done_past), `ignore_complete`. Date validation: `date_from` + `date_to` required unless `ops_manager`, `ops_exec`, or `tour_generic_code` is set.
- `tour_generic_code` filter now active in WHERE clause via subquery through `tour_series → tour_generics` (was silently ignored before). Dapper params also wired for `tour_generic_code` in both `DeparturesEndpoint` and `DashboardEndpoints`.
- New `POST /api/v1/grouptour-task-departures-facets`: returns `branches`, `managers`, `execs`, `tour_generics` (live DB query), `tour_series`, `total_matching`.
- New `POST /api/v1/grouptour-task-departures-summary`: KPI counts (total, at_risk, ready, overdue, due_later, done_today, done_past, avg_readiness).
- New `POST /api/v1/grouptour-task-departures-tasks`: paged tasks view per departure.
- New `POST /api/v1/grouptour-task-series-aggregate`: per-series risk counts and task counts.
- New `POST /api/v1/grouptour-task-heatmap`: 28-day date grid with per-departure readiness/risk cells.
- `DepartureWhereFilters` helper extracted in `OpsGroupsDbHelper` — centralises WHERE clause building used by all 7 SQL methods; eliminates duplication.
- `FacetsTourGenericsSql` added — JOIN through `tour_series → tour_generics` with the same filter set.

**`team-members` roles/branch_code_filter:** `UsersByRoleEndpoint` lives in Nova.Presets.Api — no change needed in OpsGroups.

---

### ~~Service relocations — team-members and tour-generics to Nova.Presets.Api — DONE 2026-04-26~~
`UsersByRoleEndpoint.cs` (`POST /api/v1/users/by-role`) and `TourGenericsEndpoint.cs` (`POST /api/v1/groups/tour-generics` + `/groups/tour-generics/search`) were already built and wired in Presets.Api. Dead `TeamMembersEndpoint.cs` deleted from OpsGroups.Api. Relocated stubs removed from `RemovedEndpointsEndpoint.cs` (no 410 stubs before go-live).

---

### ~~`lock_ver` — `grouptour_departure_group_tasks` Pattern A — DONE 2026-04-26~~
V002 migration (Postgres/MariaDB/MsSql). `GroupTaskUpdateSql`/`GroupTaskConditionalUpdateSql`: `AND lock_ver = @ExpectedLockVer`, `lock_ver = lock_ver + 1`. `GroupTaskByIdSql` + `GroupTasksByDepartureIdsSql`: `lock_ver` in SELECT. `GroupTaskRow`: `int LockVer`. `HandleSingleUpdateAsync`: requires `expected_lock_ver`, 409 with `server_row` on mismatch. `HandleBulkUpdateAsync`: `old_status` replaced with `ExpectedLockVer`, returns `{ saved, conflicts }` (200 OK, no transaction).

---

### Pattern A batch — confirmed `200 OK` envelope (decision record)
**Status:** Decision recorded 2026-04-26 — no code yet  
**What:** Batch Pattern A endpoints return `200 OK` with `{ saved: [...], conflicts: [...] }` — NOT `207 Multi-Status`. Decision made during UX countersign of `backend-handoff-conflict-detection.md`. Apply to:
- `PATCH /api/v1/group-task-bulk-update-group-tasks` when lock_ver is added above
- All future OpsBookings.Api batch endpoints  
**Why deferred:** Batch conflict UI is follow-on to single-row conflict UI.

---

## Tests not yet written

| Service | Priority | Notes |
|---|---|---|
| Nova.ToDo.Api | High | No tests at all. Patterns in `docs/test-conventions.md`. |
| Nova.CommonUX.Api | Medium | Manual testing done 2026-04-18. Unit tests for auth flows wanted. |
| Nova.OpsGroups.Api | High | Postman collection exists; convert to xUnit integration tests. |

---

## Nova.Presets.Api

### ~~`lock_ver` — `group_tasks` Pattern A — DONE 2026-04-26~~
V007 migration (Postgres/MariaDB/MsSql): `ALTER TABLE presets.group_tasks ADD COLUMN IF NOT EXISTS lock_ver int NOT NULL DEFAULT 0`. `HandleSaveAsync`: requires `expected_lock_ver`, adds `AND lock_ver = @ExpectedLockVer` to WHERE and `lock_ver = lock_ver + 1` to SET; on `affected == 0` re-reads row — 404 if gone, 409 with `server_row` if lock mismatch. Success response: `{ success, code, lock_ver }`. List response: exposes `lock_ver` per task.

---

## Nova.Shared

### `ExecuteWithConcurrencyCheckAsync` ~~DONE 2026-04-25~~
`Nova.Shared/Data/ConcurrencyHelper.cs` — helper + `NextVersion`. Full pattern in `docs/concurrency-field-group-versioning.md`.
