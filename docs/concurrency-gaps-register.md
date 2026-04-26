# Concurrency Gaps Register

**Purpose:** Every endpoint listed here allows two concurrent writes to the same row to both succeed
without a 409 Conflict response. This register exists so no gap is forgotten. The intent is to fix
all of them. The only reason to leave a gap open is high implementation complexity — and even then
it must remain on this register until resolved.

**Pattern for fixing:** Add `lock_ver int NOT NULL DEFAULT 0` (or `lock_ver_{domain}` for field-group)
to the table, return it in GET/fetch responses, require it in write requests, and add `AND lock_ver = @ExpectedVersion`
to the UPDATE WHERE clause. See `docs/concurrency-field-group-versioning.md`.

**Services not listed:** Nova.Shell.Api (read-only, no write endpoints), Nova.CommonUX.Api (all
write endpoints are either token-claim operations or atomic SQL — no gaps), Nova.ToDo.Api (all five
write endpoints fully protected with `AND lock_ver = @ExpectedLockVer` — `lock_ver` column added to
`sales97.ToDo` via V002 migration, `lock_ver` returned in all responses, 2026-04-25).

---

## Nova.Presets.Api

### 1. `PATCH /api/v1/tasks/{code}` — TasksEndpoint

| | |
|---|---|
| **Table** | `presets.group_tasks` |
| **Race** | Single UPDATE with no version guard. Two admins editing the same template simultaneously — last write wins silently. |
| **Fix complexity** | Medium |
| **Fix** | Add `lock_ver int NOT NULL DEFAULT 0` to `group_tasks` (3 dialect migrations). Return `lock_ver` in the list response. Require `lock_ver` in the PATCH request body. Add `AND lock_ver = @ExpectedVersion` to the UPDATE WHERE, increment `lock_ver = @NextVersion` in SET. Use `ConcurrencyHelper.ExecuteWithConcurrencyCheckAsync`. |

---

### 2. `PATCH /api/v1/tasks/reorder` — TasksEndpoint

| | |
|---|---|
| **Table** | `presets.group_tasks` |
| **Race** | Batch UPDATE of `sort_order` with no version guard. Two admins reordering simultaneously — last write wins silently. |
| **Fix complexity** | Medium (depends on gap 1 being fixed first) |
| **Fix** | Once `lock_ver` exists on the table (gap 1), the list response includes `lock_ver` per row. The reorder request sends `lock_ver` per item. Each `sort_order` UPDATE includes `AND lock_ver = @ExpectedVersion`. If any item fails the check, rollback the transaction and return 409 with conflicting codes. |

---

### 3. `POST /api/v1/user-profile/avatar` — UploadAvatarEndpoint

| | |
|---|---|
| **Table** | `nova_auth.tenant_user_profile` (`avatar_url` column) |
| **Race** | Single UPDATE with no version guard. User-scoped (only the authenticated user can update their own avatar), so the practical risk is low — two concurrent upload sessions from the same user. |
| **Fix complexity** | Medium |
| **Fix** | Add `lock_ver_avatar int NOT NULL DEFAULT 0` to `tenant_user_profile` as a field-group column (avatar updates are independent from profile updates). Return `lock_ver_avatar` in the profile fetch response. The avatar upload is `multipart/form-data` — add `lock_ver_avatar` as a form field alongside the file. Add `AND lock_ver_avatar = @ExpectedVersion` to the UPDATE. |
| **Note** | Lowest priority on this list — user-scoped, very low concurrent access probability. |

---

### 4. `POST /api/v1/user/default-password` — DefaultPasswordEndpoint

| | |
|---|---|
| **Table** | `nova_auth.tenant_user_auth` |
| **Race** | Admin UPSERT (INSERT if row doesn't exist, UPDATE if it does). Two admins setting a default password for the same user simultaneously — last UPSERT wins silently. |
| **Fix complexity** | Medium-High |
| **Fix** | Split the UPSERT into two paths: (a) if the row exists, conditional UPDATE with `AND lock_ver = @ExpectedVersion`; (b) if the row doesn't exist, INSERT. To implement: attempt the conditional UPDATE first; if 0 rows affected, check if the row now exists — if yes, conflict (409); if no, INSERT. Requires `lock_ver` on `tenant_user_auth` and a profile-fetch step to return the current `lock_ver` to the admin UI before the action. Alternatively, accept this as a last-write-wins operation for admin-only password reset (admins setting default passwords for different users never conflict; two admins resetting the same user's password is a valid conflict but extremely rare). |

---

## Nova.OpsGroups.Api

### 5. `PATCH /api/v1/group-task-business-rules` — BusinessRulesEndpoint

| | |
|---|---|
| **Table** | `opsgroups.grouptour_task_business_rules` |
| **Race** | SELECT existing row → per-field old-value comparison in C# → UPSERT. Two admins both SELECT before either UPSERTs — both pass all field checks — last UPSERT wins. The per-field comparison is checking the right thing but doing it non-atomically. |
| **Fix complexity** | Medium |
| **Fix** | Add `lock_ver int NOT NULL DEFAULT 0` to `grouptour_task_business_rules` (3 dialect migrations). Return `lock_ver` in the fetch response. PATCH request carries `lock_ver`. Replace the per-field SELECT → C# check with a conditional UPSERT: UPDATE WHERE `lock_ver = @ExpectedVersion`, increment `lock_ver = @NextVersion`. If the row doesn't exist, INSERT with `lock_ver = 1`. Drop the per-field comparison logic entirely — `lock_ver` subsumes it. |

---

### 6. `PATCH /api/v1/grouptour-task-sla-rules` — SlaRulesEndpoint

| | |
|---|---|
| **Table** | `opsgroups.grouptour_sla_rules` |
| **Race** | Pure UPSERT per rule item with no old-value check. Intentional last-write-wins — but two admins saving overlapping rule sets simultaneously produce a silent merge where the last save's rules overwrite the first. |
| **Fix complexity** | Medium |
| **Fix** | Add `lock_ver int NOT NULL DEFAULT 0` to `grouptour_sla_rules` alongside the existing `version varchar` ETag column (they serve different purposes). Return `lock_ver` per rule in the fetch response. PATCH request sends `lock_ver` per rule. Conditional UPSERT: UPDATE WHERE `lock_ver = @ExpectedVersion`, INSERT if not exists (new rule, no prior version). Return per-rule results so the client knows which rules conflicted. |
| **Note** | The existing `version varchar` ETag on this table is a grid-staleness hint returned to the UI — it is not an integer lock token and must not be confused with `lock_ver`. |

---

### 7. `PATCH /api/v1/group-task-sla-rule-save` — SlaHierarchyEndpoint ⚠ HIGH COMPLEXITY

| | |
|---|---|
| **Table** | `opsgroups.grouptour_sla_rules` |
| **Race** | Loop 1: SELECT per grid cell + C# comparison of `old_value`. Loop 2: UPSERT per grid cell. Two concurrent saves both complete Loop 1 before either starts Loop 2 — both pass the old-value check — last UPSERT wins silently. |
| **Fix complexity** | **High** |
| **Why high** | Each grid cell is an independent UPSERT (INSERT if new, UPDATE if existing) across 3 dialects. An atomic old-value guard needs to be embedded in the UPDATE branch of the UPSERT (e.g. Postgres `ON CONFLICT DO UPDATE SET ... WHERE old_offset_days = @OldValue`) — but detecting whether 0 rows were affected because of the condition vs. a true INSERT is dialect-specific. The two-loop structure (check-all-first, apply-all-second) must be redesigned. |
| **Fix (when done)** | For each cell: attempt conditional UPDATE `WHERE group_task_sla_offset_days = @OldValue`; if 0 rows and row exists → 409 conflict for that cell; if 0 rows and row doesn't exist → INSERT (new cell, no prior value). Wrap in a single transaction with rollback on any conflict. Drop the two-loop structure. |
| **Deferral accepted** | Yes — high complexity acknowledged. Must remain on this register until resolved. |

---

### 8. `PATCH /api/v1/grouptour-task-departures/{departure_id}/group-tasks/{group_task_id}` — GroupTaskEndpoint (single)

| | |
|---|---|
| **Table** | `opsgroups.grouptour_departure_group_tasks` |
| **Race** | Single UPDATE with no status/version guard. Two ops users updating the same task simultaneously — last write wins silently. |
| **Fix complexity** | Medium |
| **Fix** | Add `lock_ver int NOT NULL DEFAULT 0` to `grouptour_departure_group_tasks` (3 dialect migrations; the bulk update was already fixed in April 2026). Return `lock_ver` in the task detail response. PATCH request carries `lock_ver`. Add `AND lock_ver = @ExpectedVersion` to `GroupTaskUpdateSql`, increment `lock_ver = @NextVersion` in SET. On 0 rows affected: EXISTS check → 404 or 409. |
| **Note** | The bulk update endpoint (`PATCH /group-task-bulk-update-group-tasks`) was already fixed to use `AND status = @OldStatus` in the WHERE clause (April 2026). Once `lock_ver` is added to the table, the bulk update should also be upgraded to use `AND lock_ver = @ExpectedVersion` instead. |

---

## Summary

| # | Service | Endpoint | Table | Complexity | Deferred? |
|---|---|---|---|---|---|
| 1 | Presets | `PATCH /tasks/{code}` | `group_tasks` | Medium | No |
| 2 | Presets | `PATCH /tasks/reorder` | `group_tasks` | Medium | No (fix 1 first) |
| 3 | Presets | `POST /user-profile/avatar` | `tenant_user_profile` | Medium | No (lowest priority) |
| 4 | Presets | `POST /user/default-password` | `tenant_user_auth` | Medium-High | No |
| 5 | OpsGroups | `PATCH /group-task-business-rules` | `grouptour_task_business_rules` | Medium | No |
| 6 | OpsGroups | `PATCH /grouptour-task-sla-rules` | `grouptour_sla_rules` | Medium | No |
| 7 | OpsGroups | `PATCH /group-task-sla-rule-save` | `grouptour_sla_rules` | **High** | **Yes** |
| 8 | OpsGroups | `PATCH .../group-tasks/{id}` (single) | `grouptour_departure_group_tasks` | Medium | No |
