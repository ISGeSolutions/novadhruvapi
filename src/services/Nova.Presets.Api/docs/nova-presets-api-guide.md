# Nova.Presets.Api — Developer Guide

**Port:** 5103  
**Postman collection:** `postman/postman-Nova.Presets.Api.json`  
**UX change log:** `src/services/Nova.Presets.Api/docs/presets-api-ux-changelog.md`

---

## What This Service Is

`Nova.Presets.Api` owns user-level configuration and identity concerns:

| Endpoint | Purpose |
|---|---|
| `POST /api/v1/user-profile` | Fetch user profile (name, email, avatar, status, permissions) |
| `POST /api/v1/user-profile/avatar` | Upload profile avatar image |
| `POST /api/v1/user-profile/status-options` | Fetch tenant/company/branch-scoped status options |
| `PATCH /api/v1/user-profile/status` | Update user presence status |
| `POST /api/v1/user-profile/change-password` | Initiate password change (sends confirmation email) |
| `POST /api/v1/user-profile/confirm-password-change` | Confirm password change via email token |
| `POST /api/v1/user/default-password` | Admin: reset user to default password |
| `POST /api/v1/branches` | Fetch branches for the authenticated tenant |
| `POST /api/v1/users/by-role` | Fetch ops team members filtered by role(s) and optional branch list |
| `POST /api/v1/groups/tour-generics` | Fetch full tour generics catalogue (client-side Fuse.js search) |
| `POST /api/v1/groups/tour-generics/search` | Server-side LIKE typeahead search across tour generics |
| `POST /api/v1/tasks` | List group task templates for the tenant |
| `PATCH /api/v1/tasks/{code}` | Partial update or soft-delete a task template |
| `PATCH /api/v1/tasks/reorder` | Atomically reorder task templates (drag-drop sort order) |

Reads from two databases: **AuthDb** (`nova_auth` schema — identity/credentials) and **PresetsDb** (`presets` schema — status, password change requests, status options, task templates, tour generics, permissions).

---

## Configuration Files

| File | Purpose | Reload |
|---|---|---|
| `appsettings.json` | Encrypted connection strings, JWT, API keys | Restart required |
| `opsettings.json` | Logging, rate limiting, email sender/display settings, SQL logging | Hot-reload via `IOptionsMonitor` |

### appsettings.json — required sections

```jsonc
{
  "AuthDb":     { "ConnectionString": "<encrypted>", "DbType": "MsSql" },
  "PresetsDb":  { "ConnectionString": "<encrypted>", "DbType": "MsSql" },
  "AvatarStorage": {
    "LocalDirectory": "/tmp/nova/avatars",   // ← where files are saved on disk
    "PublicBaseUrl":  "http://localhost:5103/avatars"  // ← base of returned avatar_url
  },
  "AppBaseUrl": "http://localhost:3000",     // ← used to build the password-change email link
  "Email": {
    "SendGrid": {
      "ApiKey": "<encrypted>"               // ← omit or leave blank to use NoOpEmailSender
    }
  }
}
```

### opsettings.json — email display settings

```jsonc
{
  "Email": {
    "Provider": "SendGrid",
    "SendGrid": {
      "SenderAddress":     "noreply@novaplatform.io",
      "SenderDisplayName": "Nova Platform"
    }
  }
}
```

---

## Avatar Storage

Avatar files are served as static files by the service itself.

**Save path:**
```
{AvatarStorage.LocalDirectory}/{tenantId}/{userId}.{ext}
```

**Returned URL:**
```
{AvatarStorage.PublicBaseUrl}/{tenantId}/{userId}.{ext}
```

Example — tenant `BTDK`, user `USR001`, uploading a JPEG:
- Saved to: `/tmp/nova/avatars/BTDK/USR001.jpg`
- URL returned: `http://localhost:5103/avatars/BTDK/USR001.jpg`

`Program.cs` mounts `LocalDirectory` as a static file provider at the `/avatars` path, so `PublicBaseUrl` must always point to this service's own base URL + `/avatars`.

**Rules:**
- Accepted types: `image/jpeg`, `image/png`, `image/webp`
- Maximum size: 5 MB
- Re-uploading overwrites the previous file for the same user + extension
- `tenant_id` and `user_id` are read from JWT claims only — there is no JSON body on this endpoint

**Postman testing:**
1. Body tab → **form-data**
2. Key: `avatar`, type: **File** (use the dropdown on the key field)
3. Value: select a `.jpg`, `.png`, or `.webp` from disk
4. Do **not** set `Content-Type` manually — Postman sets the multipart boundary automatically

> **macOS caution:** Do not set `LocalDirectory` to a Windows-style path (e.g. `C:\nova\avatars`). On macOS the backslashes are treated as filename characters, creating a literal directory inside the project tree that breaks MSBuild's glob expansion. Use a Unix path such as `/tmp/nova/avatars`.

---

## Email — SendGrid / NoOp

The `change-password` flow sends a confirmation email. The sender is resolved at startup:

- If `Email:SendGrid:ApiKey` is **set** in `appsettings.json` → `SendGridEmailSender` is used
- If the key is **absent or empty** → `NoOpEmailSender` is used — the email is logged as a `Warning` instead of delivered

`NoOpEmailSender` is intentional for local development without a SendGrid key. The log entry contains the full email body including the confirmation token, so the `confirm-password-change` endpoint can still be tested end-to-end.

**Token note:** the confirmation link in the email body contains a URL-encoded token (e.g. `%2B` for `+`). When testing manually in Postman, paste the **decoded** token — replace `%2B` → `+` and `%2F` → `/`. The `confirm-password-change` endpoint also accepts the URL-encoded form and decodes it automatically.

---

## Password Conventions

| Scenario | Default password format | Example |
|---|---|---|
| Admin sets default via `/user/default-password` | `changeMe@ddMMM` | `changeMe@11Apr` |

- Hashed via Argon2id before storage
- `must_change_password` is set to `true` — user is forced to change on first login
- `ConfirmPasswordChangeEndpoint` clears `must_change_password` on successful confirmation

---

## Database Migrations

Migrations are run manually via the unversioned admin endpoint (not on startup):

```
POST http://localhost:5103/run-presets-migrations
```

Migrations live in:
```
src/services/Nova.Presets.Api/Migrations/
  MsSql/    V001__CreatePresets.sql               — company, branch, tenant_user_status,
            V002__AddUserStatusOptions.sql           tenant_password_change_requests
            V003__AddPrograms.sql                  — user_status_options
            V004__AddGroupTasksAndTourGenerics.sql — programs, program_tree
            V005__RenameOpsTasksMenuEntry.sql       — group_tasks, tour_generics, tenant_user_permissions
  Postgres/ (same V001–V005)                      — renames OPS_ACTIVITIES → OPS_TASKS program entry
  MariaDb/  (same V001–V005)
```

| Migration | Tables added / changed |
|---|---|
| V001 | `company`, `branch`, `tenant_user_status`, `tenant_password_change_requests` |
| V002 | `user_status_options` |
| V003 | `programs`, `program_tree` |
| V004 | `group_tasks`, `tour_generics`, `tenant_user_permissions` |
| V005 | Data-only — renames `OPS_ACTIVITIES` program entry to `OPS_TASKS` |

The `must_change_password` column is in the **AuthDb** (`nova_auth` schema), managed by `Nova.CommonUX.Api` migrations:
```
src/services/Nova.CommonUX.Api/Migrations/
  .../V003__AddMustChangePassword.sql
```

---

## Status Options — Scoping Rules

`user_status_options` rows are scoped by `tenant_id`, `company_code`, and `branch_code`. The sentinel value `XXXX` means "applies to all" at that level.

| Tier | company_code | branch_code | Meaning |
|---|---|---|---|
| 1 (lowest) | `XXXX` | `XXXX` | Tenant-wide default |
| 2 | `{specific}` | `XXXX` | All branches in company |
| 3 (highest) | `{specific}` | `{specific}` | Exact branch |

When the same `status_code` exists at multiple tiers, the most specific tier wins. `frz_ind = true` rows are excluded.

---

## Users by Role

`POST /api/v1/users/by-role`

Returns ops team members for the caller's tenant and company, filtered by role code.

**Request body:**
```json
{
  "tenant_id":          "BTDK",
  "company_code":       "MAIN",
  "branch_code":        "LON",
  "user_id":            "USR001",
  "browser_locale":     "en-GB",
  "browser_timezone":   "Europe/London",
  "ip_address":         "203.0.113.1",
  "roles":              ["ops_manager", "ops_exec"],
  "branch_code_filter": ["LON", "MAN"]
}
```

- `roles` — optional; defaults to `["ops_manager", "ops_exec"]` when omitted.
- `branch_code_filter` — optional; when supplied, only users with a right on one of these branch codes are returned. When omitted, the query includes users with `branch_code = @BranchCode` (caller's own branch) or `branch_code = 'XXXX'` (all-branch wildcard).

**Response:**
```json
{
  "team_members": [
    {
      "user_id":  "MGR001",
      "name":     "Alice Smith",
      "initials": "AS",
      "roles":    ["ops_manager"]
    }
  ]
}
```

A user holding both roles appears once with both listed. Reads `nova_auth.user_security_rights` and `nova_auth.tenant_user_profile` in AuthDb.

---

## Tour Generics

**Catalogue — `POST /api/v1/groups/tour-generics`**

Returns the full active tour generics list for the tenant, ordered by name. Typically used
to pre-load a Fuse.js index for client-side fuzzy search.

**Response:**
```json
{
  "tour_generics": [
    { "code": "BHU", "name": "Bhutan Cultural" },
    { "code": "NEP", "name": "Nepal Base Camp" }
  ]
}
```

**Typeahead search — `POST /api/v1/groups/tour-generics/search`**

Server-side LIKE search fallback for large catalogues or when `tenantConfig.search.tgMode === 'like'`.

**Additional request fields:**
```json
{
  "query": "nepal",
  "field": "name",
  "limit": 20
}
```

- `field`: `"name"` (default) or `"code"`
- `limit`: 1–100, defaults to 20
- `query`: empty string returns an empty list immediately (no DB hit)

Response shape is identical to the catalogue endpoint.

---

## Group Task Templates

Three endpoints share the `presets.group_tasks` table.

**List — `POST /api/v1/tasks`**

Returns all task templates for the tenant. `sort_order` controls display order; rows with
`sort_order = NULL` sort last. `frz_ind = true` rows are excluded unless
`include_frozen: true` is sent.

**Additional request field:**
```json
{ "include_frozen": false }
```

**Response:**
```json
{
  "tasks": [
    {
      "code":                       "PRE_DOCS",
      "name":                       "Pre-departure documents",
      "required":                   true,
      "critical":                   false,
      "group_task_sla_offset_days": -7,
      "reference_date":             "departure",
      "source":                     "GLOBAL",
      "sort_order":                 1,
      "frz_ind":                    false
    }
  ]
}
```

**Save — `PATCH /api/v1/tasks/{code}`**

Partial update — only include fields to change. Setting `frz_ind: true` soft-deletes the
template (no hard-delete endpoint exists).

**Request body (all fields optional):**
```json
{
  "tenant_id":                  "BTDK",
  "company_code":               "MAIN",
  "branch_code":                "LON",
  "user_id":                    "USR001",
  "browser_locale":             "en-GB",
  "browser_timezone":           "Europe/London",
  "ip_address":                 "203.0.113.1",
  "name":                       "Updated task name",
  "required":                   true,
  "critical":                   false,
  "group_task_sla_offset_days": -5,
  "reference_date":             "return",
  "source":                     "TG",
  "frz_ind":                    false
}
```

Returns `{ "success": true }` on success, 404 if the code is not found for this tenant.

**Reorder — `PATCH /api/v1/tasks/reorder`**

Atomically updates `sort_order` for a list of task codes within a single transaction.
All codes must exist for this tenant — if any are unknown, the whole request is rejected
with 409 and an `unknown_codes` extension listing the bad values.

**Additional request field:**
```json
{
  "order": [
    { "code": "PRE_DOCS", "sort_order": 1 },
    { "code": "VIS_CHECK", "sort_order": 2 }
  ]
}
```

Returns `{ "success": true }` on success.
