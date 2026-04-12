# Nova.Presets.Api — API Specification

**Port:** 5103  
**Purpose:** User profile management and branch/context presets. Sets up per-user context after login — profile display, status, avatar, and available branches.  
**Dependencies:** Nova.CommonUX.Api (auth token required for most endpoints).

---

## Architecture Notes

Presets.Api has **two database connections**:

### 1. AuthDb → `nova_auth` (read-only for profile and auth)
Shared with CommonUX.Api. Presets.Api reads from it; it never writes to `nova_auth` tables except for `tenant_user_auth.password_hash` on password-change confirmation.

Existing `nova_auth` tables read by Presets.Api:
- `tenant_user_profile` — display_name, email, avatar_url
- `tenant_user_auth` — password_hash (verify current password; write new hash on confirm)

### 2. PresetsDb → `presets` database (read/write for all presets-owned data)
This is Presets.Api's own database. It contains the legacy Company and Branch tables (MSSQL — pre-existing, not created by migrations) and new Nova tables added via Presets.Api's own migration scripts.

| Dialect | Database | Schema/prefix | Example table ref |
|---------|----------|---------------|-------------------|
| MSSQL | `presets` | `dbo` | `presets.dbo.Branch` |
| Postgres | app database | `presets` | `presets.branch` |
| MariaDB | `presets` | (none) | `presets.branch` |

**No `AddNovaTenancy()`. No `TenantResolutionMiddleware`. No per-tenant DB.**

The `tenant_id` is always extracted from the JWT `tenant_id` claim and cross-validated against the request body `tenant_id`.

Migrations run against the `presets` database via `RunPresetsMigrationsEndpoint` using the same synthetic `TenantRecord` pattern as CommonUX.Api. MSSQL legacy tables (Company, Branch) already exist — V001 scripts for MSSQL are CREATE-IF-NOT-EXISTS safe but will no-op for those tables; only the new Nova tables are actually created.

### appsettings.json (both connections)
```json
{
  "AuthDb": {
    "ConnectionString": "<encrypted nova_auth connection string>",
    "DbType": "Postgres"
  },
  "PresetsDb": {
    "ConnectionString": "<encrypted presets connection string>",
    "DbType": "Postgres"
  }
}
```

Two `IOptions<T>` config classes: `AuthDbSettings` (existing pattern) and `PresetsDbSettings` (new, same shape).

---

## Database Schema

### Legacy tables (MSSQL only — pre-existing, read by Presets.Api)

#### presets.dbo.Company
| Column | Type | Notes |
|--------|------|-------|
| CompanyCode | nvarchar(4) PK | |
| CompanyName | nvarchar(50) NOT NULL | |
| TenantId | nvarchar(50) NOT NULL | tenant scoping column |
| FrzInd | bit NOT NULL DEFAULT 0 | |

#### presets.dbo.Branch
| Column | Type | Notes |
|--------|------|-------|
| BranchCode | nvarchar(4) PK (with CompanyCode) | |
| CompanyCode | nvarchar(4) NOT NULL FK → Company | |
| BranchName | nvarchar(50) NOT NULL | |
| FrzInd | bit NOT NULL DEFAULT 0 | |

PK for Branch is (CompanyCode, BranchCode). Branch does not hold TenantId — scoped via the Company join.

---

### New tables added to `presets` database via V001 migration

New tables follow standard Nova naming: snake_case columns, standard audit fields, dialect-correct boolean/datetime/uuid types.

| Dialect | Table ref example |
|---------|-------------------|
| MSSQL | `presets.dbo.tenant_user_status` |
| Postgres | `presets.tenant_user_status` |
| MariaDB | `presets.tenant_user_status` |

#### tenant_user_status
| Column | Type | Notes |
|--------|------|-------|
| tenant_id | varchar(50) PK | |
| user_id | varchar(50) PK | |
| status_id | varchar(50) NOT NULL | validated against static options list |
| status_label | varchar(200) NOT NULL | denormalised label stored at write time |
| status_note | varchar(200) NULL | free text, max 200 chars |
| frz_ind | bool/bit NOT NULL DEFAULT false | |
| created_by | varchar(50) NOT NULL | |
| created_on | datetime NOT NULL | |
| updated_by | varchar(50) NOT NULL | |
| updated_on | datetime NOT NULL | |
| updated_at | varchar(150) NOT NULL | |

#### tenant_password_change_requests
| Column | Type | Notes |
|--------|------|-------|
| id | uuid / uniqueidentifier PK | |
| tenant_id | varchar(50) NOT NULL | |
| user_id | varchar(50) NOT NULL | |
| new_password_hash | varchar(500) NOT NULL | Argon2id PHC format |
| token_hash | varchar(500) NOT NULL | `Convert.ToHexString(SHA256.HashData(UTF8(token)))` |
| expires_on | datetime NOT NULL | `UtcNow + opsettings ChangePassword.TokenExpiryMinutes` |
| confirmed_on | datetime NULL | NULL = not yet confirmed |
| created_on | datetime NOT NULL | |

Index: `ix_password_change_requests_token` on `(token_hash)` — used for confirmation lookup.
Index: `ix_password_change_requests_user` on `(tenant_id, user_id)` — used for cleanup of old requests.

---

### Postgres/MariaDB equivalents for legacy Company/Branch tables

New deployments (Postgres, MariaDB) do not have a legacy presets database. V001 migration creates all tables from scratch using standard Nova naming conventions.

#### presets.company (Postgres schema) / `presets`.`company` (MariaDB database)
| Column | Type | Notes |
|--------|------|-------|
| company_code | varchar(4) PK | |
| tenant_id | varchar(50) NOT NULL | |
| company_name | varchar(50) NOT NULL | |
| frz_ind | boolean/TINYINT(1) NOT NULL DEFAULT false/0 | |

#### presets.branch (Postgres schema) / `presets`.`branch` (MariaDB database)
| Column | Type | Notes |
|--------|------|-------|
| branch_code | varchar(4) PK (with company_code) | |
| company_code | varchar(4) NOT NULL FK → company | |
| branch_name | varchar(50) NOT NULL | |
| frz_ind | boolean/TINYINT(1) NOT NULL DEFAULT false/0 | |

PK: (company_code, branch_code). No tenant_id on branch — scoped via company join.

---

## Configuration

### appsettings.json (additions over the platform default)
```json
{
  "AuthDb": {
    "ConnectionString": "<Nova.Cipher encrypted connection string>",
    "DbType": "Postgres"
  },
  "AvatarStorage": {
    "LocalDirectory": "/var/nova/avatars",
    "PublicBaseUrl": "http://localhost:5103/avatars"
  }
}
```

### opsettings.json
```json
{
  "ChangePassword": {
    "TokenExpiryMinutes": 60
  },
  "Email": {
    "Provider": "SendGrid",
    "SendGrid": {
      "ApiKey": "<encrypted>",
      "SenderAddress": "noreply@novaplatform.io",
      "SenderDisplayName": "Nova Platform"
    }
  }
}
```

`ChangePassword.TokenExpiryMinutes` is hot-reloadable via `IOptionsMonitor<ChangePasswordSettings>`.

---

## Common Rules

- All endpoints `POST` (reads) or `PATCH` (writes) — except `confirm-password-change` which is `POST` with no Bearer token
- All JSON request bodies include standard RequestContext: `tenant_id`, `company_id`, `branch_id`, `user_id`, `browser_locale`, `browser_timezone`, `ip_address`
- Exception: Upload Avatar uses `multipart/form-data` — no JSON body, bearer token in Authorization header only
- Exception: Confirm Password Change has no Bearer token (email link flow)
- Wire format: snake_case JSON
- Error format: RFC 9457 ProblemDetails

---

## Endpoints

### User Profile

---

#### POST /api/v1/user-profile

**Purpose:** Fetch the current user's profile including name, email, avatar, and current presence status.  
**Auth required:** Yes.

**Request body:** Standard RequestContext fields only.

**Response (200):**
```json
{
  "user_id": "user-123",
  "name": "Jane Doe",
  "email": "jane@example.com",
  "avatar_url": "https://cdn.example.com/avatars/user-123.jpg",
  "status_id": "available",
  "status_label": "Available",
  "status_note": "Reviewing bookings"
}
```

**Error cases:** 401, 403, 404 (user not found in tenant_user_profile), 422, 429, 500

**DB access pattern:** inline-sql, two-table read across two connections  
**Tables:**
- AuthDb: `nova_auth.tenant_user_profile` — `display_name`, `email`, `avatar_url`
- PresetsDb: `tenant_user_status` — `status_id`, `status_label`, `status_note`

**SQL — two separate queries (cross-DB join not possible):**

Query 1 (AuthDb):
```sql
SELECT user_id, display_name, email, avatar_url
FROM   {profile_table}
WHERE  tenant_id = @TenantId
AND    user_id   = @UserId
AND    frz_ind   = {false_literal}
```

Query 2 (PresetsDb):
```sql
SELECT status_id, status_label, status_note
FROM   {status_table}
WHERE  tenant_id = @TenantId
AND    user_id   = @UserId
```

**Business logic notes:**
- `tenant_id` extracted from JWT `tenant_id` claim; must equal request body `tenant_id` — 403 if mismatch.
- Run both queries. 404 if profile query returns no row.
- If status query returns no row: default to `status_id = "available"`, `status_label = "Available"`, `status_note = null`. No INSERT of a default row.
- `avatar_url` returned as stored (full absolute URL). NULL returned as-is if not set.
- Map `display_name` → `name` in the response.

---

#### POST /api/v1/user-profile/avatar

**Purpose:** Upload a new avatar image. Uses `multipart/form-data`, not JSON. No RequestContext fields in body — context comes from the Bearer token.  
**Auth required:** Yes (Bearer token in Authorization header).

**Request body:** `multipart/form-data`
| Field | Type | Required | Notes |
|---|---|---|---|
| avatar | file | yes | JPEG, PNG, or WebP; max 5 MB |

**Response (200):**
```json
{ "avatar_url": "https://cdn.example.com/avatars/user-123.jpg" }
```

**Error cases:** 401, 403, 404 (user not found), 422 (invalid file type or size), 429, 500

**DB access pattern:** inline-sql, single UPDATE  
**Tables:** `nova_auth.tenant_user_profile` — UPDATE `avatar_url` WHERE `tenant_id` AND `user_id`

**Business logic notes:**
- `tenant_id` and `user_id` extracted from JWT claims only (no JSON body).
- Accepted MIME types: `image/jpeg`, `image/png`, `image/webp`. Any other → 422.
- File size limit: 5 MB (checked via `IFormFile.Length`). Over limit → 422.
- Extension derived from MIME type: `.jpg` / `.png` / `.webp`.
- Save path: `{AvatarStorage.LocalDirectory}/{tenantId}/{userId}.{ext}`
  - Create directory if it does not exist.
  - If a file already exists at that path, overwrite it (no deletion of other extensions — same path used each time for a given user).
- Update `tenant_user_profile.avatar_url = {AvatarStorage.PublicBaseUrl}/{tenantId}/{userId}.{ext}`.
- Return `{ "avatar_url": "<full_url>" }`.
- Static file serving: `app.UseStaticFiles(new StaticFileOptions { ... })` maps `LocalDirectory` under the `/avatars` virtual path. On prod, set `PublicBaseUrl` to a CDN origin.

---

#### POST /api/v1/user-profile/status-options

**Purpose:** Fetch the list of available user presence status options for the status dropdown.  
**Auth required:** Yes.

**Request body:** Standard RequestContext fields only.

**Response (200):** Array of status options.
```json
[
  { "id": "available",    "label": "Available",       "colour": "#22c55e" },
  { "id": "busy",         "label": "Busy",             "colour": "#ef4444" },
  { "id": "in-meeting",   "label": "In a Meeting",     "colour": "#f59e0b" },
  { "id": "out-of-office","label": "Out of Office",    "colour": "#6b7280" },
  { "id": "dnd",          "label": "Do Not Disturb",   "colour": "#dc2626" }
]
```

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** None — static list returned from code.  
**Tables:** None.

**Business logic notes:**
- Status options are platform-wide fixed values — not tenant-configurable, not stored in DB.
- Defined as `internal static readonly IReadOnlyList<StatusOption> All = [...]` in a `UserStatusOptions` static class.
- No DB query. Returns the list directly.
- No 404 — always returns all 5 items.

---

#### PATCH /api/v1/user-profile/status

**Purpose:** Update the current user's presence status and optional status note.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| status_id | string | yes | Must be one of: available, busy, in-meeting, out-of-office, dnd |
| status_note | string | no | Max 200 chars. Pass null or omit to clear. |
| + standard RequestContext fields | | yes | |

**Response (200):** Full updated profile object (same shape as `POST /api/v1/user-profile`).

**Error cases:** 401, 403, 404, 422 (invalid status_id, or status_note too long), 429, 500

**DB access pattern:** inline-sql, UPSERT then re-fetch  
**Tables:**
- PresetsDb: `tenant_user_status` — UPSERT
- AuthDb: `nova_auth.tenant_user_profile` — re-fetch for response (JOIN, same as profile endpoint)

**SQL sketch:**
- MsSql: `MERGE presets.dbo.tenant_user_status AS target USING (VALUES (@TenantId, @UserId, ...)) AS source ... WHEN MATCHED THEN UPDATE ... WHEN NOT MATCHED THEN INSERT ...`
- Postgres: `INSERT INTO presets.tenant_user_status (...) VALUES (...) ON CONFLICT (tenant_id, user_id) DO UPDATE SET ...`
- MariaDB: `INSERT INTO presets.tenant_user_status (...) VALUES (...) ON DUPLICATE KEY UPDATE ...`

**Business logic notes:**
- Validate `status_id` against `UserStatusOptions.All` → 422 if not found.
- Validate `status_note` length ≤ 200 chars → 422 if too long.
- `status_label` denormalised from `UserStatusOptions.All` at write time (lookup by `status_id`).
- `tenant_id` from JWT claim must match request body `tenant_id`.
- After UPSERT: fetch the profile row from AuthDb (same query as `/user-profile`) and combine with the just-written status values to build the response — no second PresetsDb query needed.
- No Socket.IO event emitted from Presets.Api — Analytics.Api handles active-users-updated broadcasting independently.

---

#### POST /api/v1/user-profile/change-password

**Purpose:** Request a password change. Verifies the current password, hashes the new one, and sends a confirmation email. The new password only takes effect after the user clicks the email confirmation link.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| current_password | string | yes | Verified against existing Argon2id hash |
| new_password | string | yes | Min 8 chars, 1 upper, 1 lower, 1 number |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{ "message": "A confirmation email has been sent to your registered email address. Your new password will take effect once you confirm via the email link." }
```

**Error cases:** 401 (wrong current_password), 403, 404, 422, 429, 500

**DB access pattern:** inline-sql, SELECT + DELETE + INSERT  
**Tables:**
- AuthDb: `nova_auth.tenant_user_auth` — SELECT `password_hash` to verify current password
- AuthDb: `nova_auth.tenant_user_profile` — SELECT `email` to send confirmation email
- PresetsDb: `tenant_password_change_requests` — DELETE old rows then INSERT new request

**Business logic notes:**
1. Load `tenant_user_auth` row for (`tenant_id`, `user_id`). 404 if not found.
2. Verify `current_password` via `Argon2idHasher.Verify()`. Return 401 ProblemDetails `"wrong_password"` if fails.
3. Validate `new_password` regex: `^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$` → 422 if fails.
4. Hash `new_password` with `Argon2idHasher.Hash()`.
5. Generate `confirmationToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))`.
6. Compute `tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(confirmationToken)))`.
7. DELETE existing unexpired rows in `tenant_password_change_requests` for (`tenant_id`, `user_id`) — only one pending request allowed per user.
8. INSERT into `tenant_password_change_requests`: `new_password_hash`, `token_hash`, `expires_on = UtcNow + TokenExpiryMinutes`.
9. Load `email` from `tenant_user_profile`.
10. Send email via `IEmailSender` with a link containing `confirmationToken`. The link target is the frontend app (configurable `AppSettings.AppBaseUrl`), which calls `POST /api/v1/user-profile/confirm-password-change`.
11. Return 200.

---

#### POST /api/v1/user-profile/confirm-password-change

**Purpose:** Confirm a pending password change using the token received via email. No Bearer token required — this is the endpoint the frontend calls when the user clicks the confirmation link.  
**Auth required:** No.

**Note:** Token is received as a query parameter or path segment in the frontend confirmation link; the frontend extracts it and calls this endpoint in the request body.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| token | string | yes | The raw confirmation token from the email link |

**Response (200):**
```json
{ "message": "Your password has been updated successfully." }
```

**Error cases:** 400 (invalid or expired token), 422, 429, 500

**DB access pattern:** inline-sql, SELECT + UPDATE + UPDATE  
**Tables:**
- PresetsDb: `tenant_password_change_requests` — SELECT by `token_hash`, UPDATE `confirmed_on`
- AuthDb: `nova_auth.tenant_user_auth` — UPDATE `password_hash`

**Business logic notes:**
1. Compute `tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)))`.
2. SELECT from `tenant_password_change_requests` WHERE `token_hash = @TokenHash AND confirmed_on IS NULL AND expires_on > @UtcNow`. If not found → 400 ProblemDetails `"invalid_or_expired_token"`.
3. UPDATE `tenant_user_auth` SET `password_hash = @NewPasswordHash, updated_by = 'system', updated_on = @UtcNow, updated_at = @UpdatedAt` WHERE `tenant_id = @TenantId AND user_id = @UserId`.
4. UPDATE `tenant_password_change_requests` SET `confirmed_on = @UtcNow` WHERE `id = @Id`.
5. Return 200.

---

### Configuration

---

#### POST /api/v1/branches

**Purpose:** Fetch the list of branches (with their parent company) available to the current tenant. A tenant may have multiple companies, each with multiple branches. Used to populate branch/company selection dropdowns after login.  
**Auth required:** Yes.

**Request body:** Standard RequestContext fields only. `company_id` in RequestContext is the user's _current_ branch context — it is NOT used to filter this list (the list returns all companies/branches for the tenant).

**Response (200):** Flat array — each item carries both branch and company fields.
```json
[
  { "branch_code": "LON", "branch_name": "London",     "company_code": "ABC", "company_name": "ABC Corp" },
  { "branch_code": "MAN", "branch_name": "Manchester",  "company_code": "ABC", "company_name": "ABC Corp" },
  { "branch_code": "EDI", "branch_name": "Edinburgh",   "company_code": "XYZ", "company_name": "XYZ Ltd"  }
]
```

**Error cases:** 401, 403, 404 (no branches found for tenant), 422, 429, 500

**DB:** PresetsDb (not AuthDb)  
**DB access pattern:** inline-sql, JOIN  
**Tables:**
- MSSQL: `presets.dbo.Branch br INNER JOIN presets.dbo.Company co ON co.CompanyCode = br.CompanyCode`
- Postgres: `presets.branch br INNER JOIN presets.company co ON co.company_code = br.company_code`
- MariaDB: `presets.branch br INNER JOIN presets.company co ON co.company_code = br.company_code`

**SQL — MSSQL (legacy column names):**
```sql
SELECT br.BranchCode, br.BranchName, co.CompanyCode, co.CompanyName
FROM   presets.dbo.Branch  br
INNER  JOIN presets.dbo.Company co ON co.CompanyCode = br.CompanyCode
WHERE  co.TenantId        = @TenantId
AND    ISNULL(br.FrzInd,0) = 0
AND    ISNULL(co.FrzInd,0) = 0
ORDER  BY br.BranchName
```

**SQL — Postgres/MariaDB (standard Nova column names):**
```sql
SELECT br.branch_code, br.branch_name, co.company_code, co.company_name
FROM   {branch_table} br
INNER  JOIN {company_table} co ON co.company_code = br.company_code
WHERE  co.tenant_id = @TenantId
AND    br.frz_ind   = {false_literal}
AND    co.frz_ind   = {false_literal}
ORDER  BY br.branch_name
```

**Business logic notes:**
- Filter is on `tenant_id` only — returns ALL companies and branches for the tenant.
- No user-level permission filtering — all active branches are returned regardless of the requesting user's current branch context.
- `frz_ind` may be NULL in legacy MSSQL data — use `ISNULL(col, 0) = 0` for MSSQL; standard `= false` for Postgres/MariaDB (NULLs should not exist in new data).
- Ordered by `branch_name` / `BranchName` ascending.
- 404 if result set is empty.
- Response maps: `BranchCode` → `branch_code`, `BranchName` → `branch_name`, `CompanyCode` → `company_code`, `CompanyName` → `company_name`.

---

## Migration Notes

Presets.Api runs DbUp migration scripts against the **`presets` database** (PresetsDb connection). Migration scripts are embedded resources in `Migrations/MsSql/`, `Migrations/Postgres/`, `Migrations/MariaDb/`.

**V001__CreatePresets.sql** — behaviour differs by dialect:

| Dialect | Company / Branch tables | New Nova tables |
|---------|------------------------|-----------------|
| MSSQL | `IF OBJECT_ID(...) IS NULL` guards — no-op if legacy tables exist | Created fresh |
| Postgres | CREATE TABLE IF NOT EXISTS — creates on first deploy | Created fresh |
| MariaDB | CREATE TABLE IF NOT EXISTS — creates on first deploy | Created fresh |

New tables created by V001: `tenant_user_status`, `tenant_password_change_requests` (all three dialects).

The `RunPresetsMigrationsEndpoint` uses the same synthetic `TenantRecord` pattern:
```csharp
var presetsTenant = new TenantRecord
{
    TenantId = "nova-presets", DisplayName = "Nova Presets Database",
    DbType = presetsDb.DbType, ConnectionString = presetsDb.ConnectionString,
    SchemaVersion = "v1", BrokerType = BrokerType.RabbitMq
};
```

---

## Summary of Endpoints

| Method | Route | Auth | Notes |
|--------|-------|------|-------|
| POST | /api/v1/user-profile | Bearer | Fetch profile |
| POST | /api/v1/user-profile/avatar | Bearer (form-data) | Upload avatar |
| POST | /api/v1/user-profile/status-options | Bearer | Static list |
| PATCH | /api/v1/user-profile/status | Bearer | Update status |
| POST | /api/v1/user-profile/change-password | Bearer | Initiate change |
| POST | /api/v1/user-profile/confirm-password-change | None | Confirm via email token |
| POST | /api/v1/branches | Bearer | Fetch branch list |
| GET | /health | None | Liveness |
| GET | /health/db | None | nova_auth connectivity |
| POST | /internal/run-migrations | Internal | Run DbUp migrations |
