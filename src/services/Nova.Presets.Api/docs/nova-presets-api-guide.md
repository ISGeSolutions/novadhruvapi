# Nova.Presets.Api — Developer Guide

**Port:** 5103  
**Postman collection:** `postman/postman-Nova.Presets.Api.json`  
**UX change log:** `src/services/Nova.Presets.Api/docs/presets-api-ux-changelog.md`

---

## What This Service Is

`Nova.Presets.Api` owns user-level configuration and identity concerns:

| Endpoint | Purpose |
|---|---|
| `POST /api/v1/user-profile` | Fetch user profile (name, email, avatar, status) |
| `POST /api/v1/user-profile/avatar` | Upload profile avatar image |
| `POST /api/v1/user-profile/status-options` | Fetch tenant/company/branch-scoped status options |
| `PATCH /api/v1/user-profile/status` | Update user presence status |
| `POST /api/v1/user-profile/change-password` | Initiate password change (sends confirmation email) |
| `POST /api/v1/user-profile/confirm-password-change` | Confirm password change via email token |
| `POST /api/v1/user/default-password` | Admin: reset user to default password |
| `POST /api/v1/branches` | Fetch branches for the authenticated tenant |

Reads from two databases: **AuthDb** (`nova_auth` schema — identity/credentials) and **PresetsDb** (`presets` schema — status, password change requests, status options).

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
  MsSql/    V001__CreatePresets.sql
            V002__AddUserStatusOptions.sql
  Postgres/ V001__CreatePresets.sql
            V002__AddUserStatusOptions.sql
  MariaDb/  V001__CreatePresets.sql
            V002__AddUserStatusOptions.sql
```

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
