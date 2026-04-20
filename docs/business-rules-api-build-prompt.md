# Business Rules API — Backend Build Brief

A self-contained brief for the backend team (or an AI assistant such as Claude) to implement and test the two new **Business Rules** endpoints in the `Nova.OpsGroups.Api` service (`:5106`).

Drop this file into the same chat session as `docs/api-specification.md` (Business Rules section) and `docs/postman-Nova.OpsGroups.Api.json` for full context.

---

## 1. Scope

Two endpoints, both at the same path:

| # | Method | Path                                       | Purpose                              |
|---|--------|--------------------------------------------|--------------------------------------|
| 1 | `POST` | `/api/v1/grouptour-task-business-rules`    | Fetch the tenant's current rules     |
| 2 | `PATCH`| `/api/v1/grouptour-task-business-rules`    | Update one or more rule fields       |

Both are tenant-scoped (one row per `tenant_id` + `company_code` + `branch_code`), JWT-protected (`Authorization: Bearer <access_token>`), and follow project-wide conventions (snake_case wire format, RFC 9457 errors, auto-injected context).

---

## 2. Data model

### Table: `grouptour_task_business_rules`

| Column                     | Type           | Notes                                                  |
|----------------------------|----------------|--------------------------------------------------------|
| `tenant_id`                | varchar(16)    | PK part 1                                              |
| `company_code`             | varchar(16)    | PK part 2                                              |
| `branch_code`              | varchar(16)    | PK part 3                                              |
| `overdue_critical_days`    | int            | ≥ 0, ≤ `overdue_warning_days`. Default `3`.            |
| `overdue_warning_days`     | int            | ≥ 0. Default `7`.                                      |
| `readiness_method`         | varchar(32)    | Enum: `required_only` \| `all_activities`. Default `required_only`. |
| `risk_red_threshold`       | varchar(64)    | Default `critical_overdue`.                            |
| `risk_amber_threshold`     | varchar(64)    | Default `any_overdue`.                                 |
| `risk_green_threshold`     | varchar(64)    | Default `no_overdue`.                                  |
| `heatmap_red_max`          | int            | 0–100, must be `< heatmap_amber_max`. Default `39`.    |
| `heatmap_amber_max`        | int            | 0–100, must be `> heatmap_red_max` and `≤ 100`. Default `79`. |
| `auto_mark_overdue`        | boolean        | Default `true`.                                        |
| `include_na_in_readiness`  | boolean        | Default `false`.                                       |
| `updated_at`               | timestamptz    | Auto-managed (`now()` on insert/update).               |
| `updated_by`               | varchar(16)    | From `user_id` in request context.                     |

If no row exists for the tenant on first fetch, return the **defaults above** (do not auto-insert until first save).

---

## 3. Request / response contracts

### 3.1 `POST /api/v1/grouptour-task-business-rules` — Fetch

**Request body** (only the auto-injected context — no domain fields):

```json
{
  "tenant_id": "T001",
  "company_code": "C001",
  "branch_code": "BR001",
  "user_id": "U001",
  "browser_locale": "en-GB",
  "browser_timezone": "Europe/London",
  "ip_address": "203.0.113.42"
}
```

**Response 200:**

```json
{
  "business_rules": {
    "overdue_critical_days": 3,
    "overdue_warning_days": 7,
    "readiness_method": "required_only",
    "risk_red_threshold": "critical_overdue",
    "risk_amber_threshold": "any_overdue",
    "risk_green_threshold": "no_overdue",
    "heatmap_red_max": 39,
    "heatmap_amber_max": 79,
    "auto_mark_overdue": true,
    "include_na_in_readiness": false,
    "updated_at": "2026-04-18T09:00:00Z",
    "updated_by": "U001"
  }
}
```

**Errors:** `401`, `500`.

### 3.2 `PATCH /api/v1/grouptour-task-business-rules` — Save

Follows the project's standard **save-payload** convention: only edited fields are sent, each with its `old` and `new` values.

**Request body:**

```json
{
  "changes": {
    "overdue_warning_days": { "old": 7, "new": 5 },
    "readiness_method":      { "old": "required_only", "new": "all_activities" },
    "heatmap_amber_max":     { "old": 79, "new": 85 }
  },
  "tenant_id": "T001",
  "company_code": "C001",
  "branch_code": "BR001",
  "user_id": "U001",
  "browser_locale": "en-GB",
  "browser_timezone": "Europe/London",
  "ip_address": "203.0.113.42"
}
```

**Response 200:** the **full updated** `business_rules` block (same shape as Fetch), including refreshed `updated_at` / `updated_by`. The frontend uses this to re-render with the server's authoritative state.

```json
{
  "business_rules": {
    "overdue_critical_days": 3,
    "overdue_warning_days": 5,
    "readiness_method": "all_activities",
    "risk_red_threshold": "critical_overdue",
    "risk_amber_threshold": "any_overdue",
    "risk_green_threshold": "no_overdue",
    "heatmap_red_max": 39,
    "heatmap_amber_max": 85,
    "auto_mark_overdue": true,
    "include_na_in_readiness": false,
    "updated_at": "2026-04-18T10:14:22Z",
    "updated_by": "U001"
  }
}
```

---

## 4. Validation rules (server-side)

Apply **after** merging `changes` into the current row, before persisting:

| Rule                                                              | On failure return                                  |
|-------------------------------------------------------------------|----------------------------------------------------|
| `overdue_critical_days` ≥ 0                                       | `422`, pointer `/changes/overdue_critical_days/new`|
| `overdue_critical_days` ≤ `overdue_warning_days`                  | `422`, pointer `/changes/overdue_critical_days/new`|
| `overdue_warning_days` ≥ 0                                        | `422`, pointer `/changes/overdue_warning_days/new` |
| `heatmap_red_max` 0–100                                           | `422`, pointer `/changes/heatmap_red_max/new`      |
| `heatmap_amber_max` 0–100                                         | `422`, pointer `/changes/heatmap_amber_max/new`    |
| `heatmap_red_max` < `heatmap_amber_max`                           | `422`, pointer `/changes/heatmap_red_max/new`      |
| `readiness_method` ∈ `{ required_only, all_activities }`          | `422`, pointer `/changes/readiness_method/new`     |
| `auto_mark_overdue`, `include_na_in_readiness` are booleans       | `422`, pointer `/changes/<field>/new`              |
| Every key under `changes` is a known business-rule field          | `422`, pointer `/changes/<unknown>`                |

**Optimistic-concurrency check (recommended):** for each key in `changes`, verify `old` matches the current persisted value. If any mismatch → `409 Conflict` with pointer `/changes/<field>/old`.

### 4.1 RFC 9457 error body

```json
{
  "type": "https://novadhruv.com/problems/validation",
  "title": "Validation failed",
  "status": 422,
  "errors": [
    { "pointer": "/changes/heatmap_red_max/new", "detail": "Must be less than heatmap_amber_max" }
  ]
}
```

---

## 5. Auto-injected context fields

Every request body carries seven context fields that the backend **must** read (and may persist for audit):

| Field             | Source                                 | Use                              |
|-------------------|----------------------------------------|----------------------------------|
| `tenant_id`       | JWT claim, echoed in body              | Row scope                        |
| `company_code`    | JWT claim, echoed in body              | Row scope                        |
| `branch_code`     | JWT claim, echoed in body              | Row scope                        |
| `user_id`         | JWT claim, echoed in body              | `updated_by` on save             |
| `browser_locale`  | Frontend                               | Optional audit log               |
| `browser_timezone`| Frontend                               | Optional audit log               |
| `ip_address`      | Frontend (or `X-Forwarded-For`)        | Optional audit log               |

If JWT claims and body context disagree, **JWT wins**.

---

## 6. Wire-format conventions (project-wide)

- All keys on the wire are **snake_case** (frontend converts to/from camelCase automatically).
- Reads use `POST` (with body) — never `GET` — so context can be sent.
- Writes use `PATCH` with the `{ "changes": { field: { old, new } } }` shape.
- Errors follow **RFC 9457 Problem Details**.
- Rate-limit responses: `429` with `Retry-After` header.

---

## 7. Test checklist (Postman or integration tests)

The two requests are already in `docs/postman-Nova.OpsGroups.Api.json` under the **Group Tour Tasks** folder.

1. **Fetch with no row in DB** → returns defaults; nothing inserted.
2. **PATCH** changing `overdue_warning_days` 7 → 5 → response shows `overdue_warning_days: 5` and a fresh `updated_at`.
3. **Fetch again** → reflects step 2.
4. **PATCH with invalid threshold** (`heatmap_red_max: 90, heatmap_amber_max: 80`) → `422` with pointer `/changes/heatmap_red_max/new`.
5. **PATCH with stale `old`** (concurrent edit) → `409 Conflict` (if implemented).
6. **PATCH with unknown field** → `422` with pointer `/changes/<unknown>`.
7. **No `Authorization` header** → `401`.

---

## 8. Frontend touchpoints (already wired — no change needed by backend)

For reference only:

- Registry keys in `src/services/apiNovaDhruvUxConfig.ts`:
  `grouptourTaskBusinessRules`, `grouptourTaskBusinessRulesSave` (both → `opsGroups`).
- Live calls in `src/services/live/grouptourTask.live.ts`:
  `fetchBusinessRulesLive()`, `saveBusinessRulesLive(changes)`.
- Page: `src/pages/BusinessRulesPage.tsx` — fetches on mount, sends only edited fields on Save.

---

## 9. Done = …

- Both endpoints respond per §3 against the Postman collection.
- Validation matrix in §4 fully implemented and surfaced as RFC 9457.
- Frontend "Business Rules" page in **Live mode** loads, edits, saves, and round-trips correctly with no console errors.
