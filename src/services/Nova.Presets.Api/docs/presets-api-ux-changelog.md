# Nova.Presets.Api — UX API Change Notice

**Date:** 2026-04-11
**Service:** Nova.Presets.Api (port 5103)
**Postman collection:** `postman/postman-Nova.Presets.Api.json`

Three changes from the original Postman stub that require frontend updates.

---

## 4. New endpoint — Set Default Password

**`POST /api/v1/user/default-password`**

Admin-only endpoint to set a user's password to the platform default, forcing a change on next login.

| | |
|---|---|
| Auth | Bearer token required. Caller must be an authenticated admin. |
| Content-Type | `application/json` |

**Request body:**
```json
{
  "tenant_id":        "BTDK",
  "company_code":     "MAIN",
  "branch_code":      "LON",
  "user_id":          "ADM001",
  "target_user_id":   "USR042",
  "browser_locale":   "en-GB",
  "browser_timezone": "Europe/London",
  "ip_address":       "203.0.113.42"
}
```

`user_id` is the calling admin. `target_user_id` is the user whose password is being reset — these are distinct fields.

**Behaviour:**
- Sets password to `changeMe@ddMMM` (e.g. `changeMe@11Apr` — server UTC date at time of call), hashed via Argon2id
- Sets `must_change_password = true` — user is forced to change on first login
- Resets `failed_login_count = 0` and clears `locked_until`
- Upserts `nova_auth.tenant_user_auth` — creates the row if it does not yet exist
- Returns 404 if `target_user_id` has no active profile in `nova_auth.tenant_user_profile`

**200 OK:**
```json
{
  "message": "Default password set. User must change password on next login."
}
```

**404 Not Found:**
```json
{
  "title": "Not found",
  "detail": "Target user profile not found.",
  "status": 404
}
```

**422 Unprocessable Entity:**
```json
{
  "title": "Validation failed",
  "errors": { "target_user_id": ["target_user_id is required."] }
}
```

---

## 5. Status Options — `id` renamed to `status_code`, now DB-driven

**`POST /api/v1/user-profile/status-options`**

The response field `id` has been renamed to `status_code`. The list is no longer hardcoded — it is loaded from `presets.user_status_options`, scoped to the caller's `tenant_id`, `company_code`, and `branch_code`.

**Old response (incorrect):**
```json
[
  { "id": "available", "label": "Available", "colour": "#22c55e" }
]
```

**Actual response:**
```json
[
  { "status_code": "available",     "label": "Available",      "colour": "#22c55e" },
  { "status_code": "busy",          "label": "Busy",            "colour": "#ef4444" },
  { "status_code": "in-meeting",    "label": "In a Meeting",    "colour": "#f59e0b" },
  { "status_code": "out-of-office", "label": "Out of Office",   "colour": "#6b7280" },
  { "status_code": "dnd",           "label": "Do Not Disturb",  "colour": "#dc2626" }
]
```

Any component binding to `.id` will need to be updated to `.status_code`. An empty array is returned if no options are configured for the tenant.

---

## 1. New endpoint — Confirm Password Change

**`POST /api/v1/user-profile/confirm-password-change`**

This endpoint was missing from the collection. It is the second leg of the change-password flow.

| | |
|---|---|
| Auth | None — `AllowAnonymous`. No Bearer token required. |
| Content-Type | `application/json` |
| Rate limited | No |

**Flow:**
1. User calls `POST /api/v1/user-profile/change-password` (authenticated) — API sends a confirmation email.
2. Email contains a link to the frontend (e.g. `/confirm-password-change?token=<token>`).
3. Frontend extracts the `token` query param and POSTs it to this endpoint.
4. On success the new password is active and the user can log in.

**Request body:**
```json
{
  "token": "<value from email link query string>"
}
```

**200 OK:**
```json
{
  "message": "Your password has been updated successfully."
}
```

**400 Bad Request** — token not found, already used, or expired:
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad request",
  "detail": "Invalid or expired confirmation token.",
  "status": 400
}
```

**422 Unprocessable Entity** — missing token field:
```json
{
  "errors": { "token": ["token is required."] }
}
```

---

## 2. Branches response shape changed

**`POST /api/v1/branches`**

The Postman stub had a placeholder shape. The real response includes both branch and company fields.

**Old stub (incorrect):**
```json
[
  { "id": "LON", "name": "London" }
]
```

**Actual response:**
```json
[
  {
    "branch_code":  "LON",
    "branch_name":  "London",
    "company_code": "MAIN",
    "company_name": "Main Company"
  }
]
```

A tenant can have multiple companies, each with multiple branches. All active branches across all companies for the tenant are returned, ordered by `branch_name` ascending. `company_code` and `company_name` are included so the frontend can group or label branches by company if needed.

---

## 3. Status Options — `colour` not `color`

**`POST /api/v1/user-profile/status-options`**

The Postman stub used `color`. The API returns `colour` (English UK spelling — consistent with all Nova identifiers).

**Old stub (incorrect):**
```json
[
  { "id": "available", "label": "Available", "color": "#22c55e" }
]
```

**Actual response:**
```json
[
  { "status_code": "available",     "label": "Available",      "colour": "#22c55e" },
  { "status_code": "busy",          "label": "Busy",            "colour": "#ef4444" },
  { "status_code": "in-meeting",    "label": "In a Meeting",    "colour": "#f59e0b" },
  { "status_code": "out-of-office", "label": "Out of Office",   "colour": "#6b7280" },
  { "status_code": "dnd",           "label": "Do Not Disturb",  "colour": "#dc2626" }
]
```

Any component binding to `.color` will need to be updated to `.colour`. Note also that
the field was renamed from `id` to `status_code` — see item 5.
