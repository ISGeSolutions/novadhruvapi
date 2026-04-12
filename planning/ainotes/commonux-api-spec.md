# Nova.CommonUX.Api — API Specification

**Terminology note:** All JSON fields, code, and config use `client_secret` — not `shared_password`. Documentation may reference both terms for context.

**Port:** 5102  
**Purpose:** Authentication hub and UX bootstrap — every client session starts here. Handles all login flows, token lifecycle, tenant config, and navigation menus.  
**Dependencies:** None (this service is the auth source for all other services).

---

## Common Rules

- All endpoints `POST` (reads + auth flows) or `PATCH` (writes)
- Auth endpoints (`/auth/*`) do NOT receive auto-injected RequestContext fields — they are the source of the token
- All other endpoints include standard RequestContext: `tenant_id`, `company_id`, `branch_id`, `user_id`, `browser_locale`, `browser_timezone`, `ip_address`
- Wire format: snake_case JSON
- Error format: RFC 9457 ProblemDetails

---

## Endpoints

### Authentication

---

#### POST /api/v1/auth/token

**Purpose:** Obtain an application-level JWT using a tenant client secret (machine-to-machine / app bootstrap).  
**Auth required:** No Bearer token.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| tenant_id | string | yes | Must exist in tenant registry |
| client_secret | string | yes | Tenant client secret |

**Response (200):**
```json
{ "token": "eyJ...", "expires_in": 3600 }
```

**Error cases:** 401 (invalid client_secret), 422 (missing fields), 429, 500

**DB access pattern:** inline-sql  
**Tables:**
| Engine | Full reference |
|---|---|
| MSSQL | `nova_auth.dbo.tenant_secrets` |
| Postgres | `nova_auth.tenant_secrets` |
| MariaDB | `` `nova_auth`.`tenant_secrets` `` |

**Business logic notes:** Load `client_secret_hash` from `tenant_secrets` by `tenant_id`. Verify incoming `client_secret` against stored Argon2id hash via Konscious. Plaintext is never stored or recoverable. Issue JWT on match.

---

#### POST /api/v1/auth/login

**Purpose:** Authenticate a user with username + password. Returns JWT directly, or a session_token if 2FA is required.  
**Auth required:** No Bearer token.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| tenant_id | string | yes | Required — user_id is not globally unique across tenants |
| user_id | string | yes | Must be min 1 char |
| password | string | yes | Must be min 8 chars |

**Response (200) — no 2FA:**
```json
{
  "token": "eyJ...", "expires_in": 3600, "requires_2fa": false, "session_token": null,
  "refresh_token": "opaque-refresh-token",
  "user": { "user_id": "U001", "name": "Jane Doe", "email": "jane@example.com", "avatar_url": "..." }
}
```

**Response (200) — 2FA required:**
```json
{ "token": "", "expires_in": 0, "requires_2fa": true, "session_token": "temp-session-token" }
```

**Error cases:** 401 (invalid credentials), 422, 429, 500

**DB access pattern:** inline-sql  
**Tables:**
| Engine | Full reference |
|---|---|
| MSSQL | `nova_auth.dbo.tenant_user_auth`, `nova_auth.dbo.tenant_user_profile` |
| Postgres | `nova_auth.tenant_user_auth`, `nova_auth.tenant_user_profile` |
| MariaDB | `` `nova_auth`.`tenant_user_auth` ``, `` `nova_auth`.`tenant_user_profile` `` |

**Business logic notes:**
1. Load `tenant_user_auth` row by `(tenant_id, user_id)`. Return 401 if not found or `frz_ind = 1`.
2. Check `locked_until` — if set and in the future, return 401.
3. Verify incoming `password` against `password_hash` using Argon2id (Konscious). On failure: increment `failed_login_count`, set `locked_until` if threshold exceeded, return 401.
4. On success: reset `failed_login_count` to 0, update `last_login_on`.
5. If `totp_enabled = true`: generate a short-lived `session_token`, return `requires_2fa: true`. Do not issue JWT yet.
6. If `totp_enabled = false`: load profile from `tenant_user_profile`, issue JWT, return full response.

**TOTP library:** `Otp.NET` (NuGet) — RFC 6238 compliant, compatible with Google Authenticator, Microsoft Authenticator, and Authy.  
**session_token storage:** Redis (or InMemory for single-instance deployments) — controlled by `CacheProvider` in `opsettings.json`. TTL = `TwoFaSessionExpiryMinutes` (default: 5).  
**Failed login lockout threshold:** `FailedLoginMaxAttempts` (default: 5) failed attempts triggers `FailedLoginLockoutMinutes` (default: 15) minute lockout. Both configurable in `opsettings.json`.

---

#### POST /api/v1/auth/verify-2fa

**Purpose:** Complete a 2FA login by verifying a TOTP code against a prior session_token.  
**Auth required:** No Bearer token.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| session_token | string | yes | From login response |
| code | string | yes | 6-digit numeric TOTP |

**Response (200):**
```json
{
  "token": "eyJ...", "expires_in": 3600, "requires_2fa": false,
  "refresh_token": "opaque-refresh-token",
  "user": { "user_id": "U001", "name": "Jane Doe", "email": "...", "avatar_url": "..." }
}
```

**Error cases:** 401 (invalid/expired session or wrong code), 422, 429, 500

**DB access pattern:** inline-sql — reads `tenant_user_auth` (for `totp_secret_encrypted`) and `tenant_user_profile`  
**Tables:**
| Engine | Full reference |
|---|---|
| MSSQL | `nova_auth.dbo.tenant_user_auth`, `nova_auth.dbo.tenant_user_profile` |
| Postgres | `nova_auth.tenant_user_auth`, `nova_auth.tenant_user_profile` |
| MariaDB | `` `nova_auth`.`tenant_user_auth` ``, `` `nova_auth`.`tenant_user_profile` `` |

**Business logic notes:**
1. Resolve `session_token` → `(tenant_id, user_id)`. Return 401 if not found or expired.
2. Load `totp_secret_encrypted` from `tenant_user_auth`. Decrypt via CipherService.
3. Verify 6-digit `code` against decrypted TOTP secret using TOTP library.
4. On success: invalidate session_token, load profile, issue JWT.

**session_token expiry window:** `TwoFaSessionExpiryMinutes` (default: 5). Configurable in `opsettings.json`.  
**TOTP tolerance window:** ±1 step (±30 seconds) — standard tolerance to account for clock drift between client and server.

---

#### POST /api/v1/auth/forgot-password

**Purpose:** Send a password reset email to the user's registered address.  
**Auth required:** No Bearer token.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| tenant_id | string | yes | |
| user_id | string | yes | |

**Response (200):** Always returns success message regardless of whether user_id exists (security by obscurity).
```json
{ "message": "If this user exists, a reset email has been sent." }
```

**Error cases:** 422, 429, 500

**DB access pattern:** inline-sql — reads `tenant_user_profile` for email, writes reset token  
**Tables:**
| Engine | Full reference |
|---|---|
| MSSQL | `nova_auth.dbo.tenant_user_profile`, `nova_auth.dbo.tenant_auth_tokens` |
| Postgres | `nova_auth.tenant_user_profile`, `nova_auth.tenant_auth_tokens` |
| MariaDB | `` `nova_auth`.`tenant_user_profile` ``, `` `nova_auth`.`tenant_auth_tokens` `` |

**Business logic notes:** Look up email from `tenant_user_profile`. Generate a cryptographically random token, hash it (SHA-256), store in `tenant_auth_tokens` with type `password_reset` and expiry. Send email with plaintext token in link. Always return 200 regardless of lookup result.

**token_expiry:** 60 minutes (`Auth.PasswordResetTokenExpiryMinutes: 60` in opsettings.json)  
**Email provider:** `IEmailSender` abstraction. Default implementation: `SendGridEmailSender` (SendGrid NuGet SDK, API key in opsettings.json, CipherService-encrypted). `MicrosoftGraphEmailSender` to be added later — signature will be provided from existing project. Active provider controlled by `Email.Provider` in opsettings.json.

---

#### POST /api/v1/auth/reset-password

**Purpose:** Reset password using the token from the reset email.  
**Auth required:** No Bearer token.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| token | string | yes | From reset email link |
| new_password | string | yes | Min 8 chars, 1 upper, 1 lower, 1 number |

**Response (200):**
```json
{ "message": "Password has been reset successfully." }
```

**Error cases:** 401 (invalid/expired token), 422, 429, 500

**DB access pattern:** inline-sql — reads `tenant_auth_tokens`, updates `tenant_user_auth`  
**Tables:**
| Engine | Full reference |
|---|---|
| MSSQL | `nova_auth.dbo.tenant_auth_tokens`, `nova_auth.dbo.tenant_user_auth` |
| Postgres | `nova_auth.tenant_auth_tokens`, `nova_auth.tenant_user_auth` |
| MariaDB | `` `nova_auth`.`tenant_auth_tokens` ``, `` `nova_auth`.`tenant_user_auth` `` |

**Business logic notes:** Hash incoming token (SHA-256), look up in `tenant_auth_tokens` by hash with type `password_reset`. Return 401 if not found or expired. Hash `new_password` via Argon2id, update `tenant_user_auth.password_hash`. Mark token as used (single-use).

**Token single-use:** Yes — mark used immediately on successful reset.  
**Session invalidation on reset:** Yes — delete all Redis refresh token entries for the user matching key pattern `refresh:{tenant_id}:{user_id}:*`. Forces re-login on all devices. Password reset is treated as a security event.

---

#### POST /api/v1/auth/magic-link

**Purpose:** Send a magic link (passwordless login) to the user's registered email.  
**Auth required:** No Bearer token.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| tenant_id | string | yes | |
| email | string | yes | Must be valid email format |

**Response (200):** Always returns success (security by obscurity).
```json
{ "message": "If this email is registered, a magic link has been sent." }
```

**Error cases:** 422, 429, 500

**DB access pattern:** inline-sql — reads `tenant_user_profile` by email, writes to `tenant_auth_tokens`  
**Tables:**
| Engine | Full reference |
|---|---|
| MSSQL | `nova_auth.dbo.tenant_user_profile`, `nova_auth.dbo.tenant_auth_tokens` |
| Postgres | `nova_auth.tenant_user_profile`, `nova_auth.tenant_auth_tokens` |
| MariaDB | `` `nova_auth`.`tenant_user_profile` ``, `` `nova_auth`.`tenant_auth_tokens` `` |

**Business logic notes:** Look up user by `(tenant_id, email)` in `tenant_user_profile`. Generate cryptographically random token, hash (SHA-256), store in `tenant_auth_tokens` with type `magic_link`. Send email with plaintext token in link. Always return 200.

**Magic link expiry:** 15 minutes (`Auth.MagicLinkTokenExpiryMinutes: 15` in opsettings.json)  
**Single-use:** Yes — consumed on verify.

---

#### POST /api/v1/auth/magic-link/verify

**Purpose:** Exchange a magic link token for a JWT.  
**Auth required:** No Bearer token.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| token | string | yes | From magic link email |

**Response (200):**
```json
{
  "token": "eyJ...", "expires_in": 3600, "requires_2fa": false,
  "refresh_token": "opaque-refresh-token",
  "user": { "user_id": "magic-user", "name": "Jane Doe", "email": "jane@example.com", "avatar_url": null }
}
```

**Error cases:** 401, 422, 429, 500

**DB access pattern:** inline-sql — reads `tenant_auth_tokens`, `tenant_user_profile`  
**Tables:**
| Engine | Full reference |
|---|---|
| MSSQL | `nova_auth.dbo.tenant_auth_tokens`, `nova_auth.dbo.tenant_user_profile` |
| Postgres | `nova_auth.tenant_auth_tokens`, `nova_auth.tenant_user_profile` |
| MariaDB | `` `nova_auth`.`tenant_auth_tokens` ``, `` `nova_auth`.`tenant_user_profile` `` |

**Business logic notes:** Hash incoming token (SHA-256), look up in `tenant_auth_tokens` by hash with type `magic_link`. Return 401 if not found or expired. Mark token as used. Load profile from `tenant_user_profile`. Issue JWT. Magic link users bypass `totp_enabled` check — no 2FA on magic link flow.

---

#### POST /api/v1/auth/social — Initiate

**Purpose:** Initiate an OAuth social login flow. Returns a redirect URL for the client to navigate to.  
**Auth required:** No Bearer token.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| provider | string | yes | One of: `google`, `microsoft`, `apple` |
| callback_url | string | yes | Must be a valid URL |

**Response (200):**
```json
{ "redirect_url": "https://accounts.google.com/o/oauth2/v2/auth?client_id=..." }
```

**Error cases:** 422, 429, 500

**DB access pattern:** None — config lookup only.  
**Business logic notes:** OAuth `client_id` and `client_secret` per provider are read from `opsettings.json` at runtime (not from DB). Build provider-specific OAuth redirect URL with `state` parameter encoding `callback_url`. No DB writes.

---

#### POST /api/v1/auth/social — Complete

**Purpose:** Exchange the OAuth callback social_token for a Nova JWT.  
**Auth required:** No Bearer token.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| tenant_id | string | yes | Required to scope the user lookup |
| provider | string | yes | One of: `google`, `microsoft`, `apple` |
| social_token | string | yes | From OAuth callback |

**Response (200):**
```json
{
  "token": "eyJ...", "expires_in": 3600, "requires_2fa": false,
  "refresh_token": "opaque-refresh-token",
  "user": { "user_id": "U001", "name": "Jane Doe", "email": "google@example.com", "avatar_url": "..." }
}
```

**Error cases:** 401 (no linked account found), 422, 429, 500

**DB access pattern:** inline-sql  
**Tables:**
| Engine | Full reference |
|---|---|
| MSSQL | `nova_auth.dbo.tenant_user_social_identity`, `nova_auth.dbo.tenant_user_profile` |
| Postgres | `nova_auth.tenant_user_social_identity`, `nova_auth.tenant_user_profile` |
| MariaDB | `` `nova_auth`.`tenant_user_social_identity` ``, `` `nova_auth`.`tenant_user_profile` `` |

**Business logic notes:**
Pre-provisioning model — no auto-creation. Admin must create the user first.

1. Verify `social_token` with the provider → extract `provider_user_id` and `provider_email`.
2. Look up `tenant_user_social_identity` by `(tenant_id, provider, provider_user_id)`.
3. If not found, look up by `(tenant_id, provider, provider_email)` where `provider_user_id IS NULL` — this is an admin-provisioned pending link.
4. If found on step 3: populate `provider_user_id` and `linked_on` — link is now fully resolved.
5. If neither found: return 401. No account creation.
6. Load profile from `tenant_user_profile`. Issue JWT.

---

#### POST /api/v1/auth/social/link — Initiate

**Purpose:** Initiate the OAuth flow to link a social provider to an already-authenticated Nova user account.  
**Auth required:** Yes (existing Bearer token).

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| provider | string | yes | One of: `google`, `microsoft`, `apple` |
| callback_url | string | yes | Must be a valid URL |

**Response (200):**
```json
{ "redirect_url": "https://accounts.google.com/o/oauth2/v2/auth?client_id=..." }
```

**Error cases:** 401, 422, 429, 500

**DB access pattern:** None — config lookup only.  
**Business logic notes:** Same redirect URL construction as `/auth/social — Initiate`. `state` parameter must encode the authenticated user's `(tenant_id, user_id)` so the complete step can resolve the account.

---

#### POST /api/v1/auth/social/link — Complete

**Purpose:** Complete social account linking by exchanging the OAuth callback token and writing the identity row.  
**Auth required:** Yes (existing Bearer token).

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| provider | string | yes | One of: `google`, `microsoft`, `apple` |
| social_token | string | yes | From OAuth callback |

**Response (200):**
```json
{ "message": "Social account linked successfully.", "provider": "google", "provider_email": "jane@gmail.com" }
```

**Error cases:** 401, 409 (provider already linked to this or another account), 422, 429, 500

**DB access pattern:** inline-sql  
**Tables:**
| Engine | Full reference |
|---|---|
| MSSQL | `nova_auth.dbo.tenant_user_social_identity` |
| Postgres | `nova_auth.tenant_user_social_identity` |
| MariaDB | `` `nova_auth`.`tenant_user_social_identity` `` |

**Business logic notes:**
1. Verify `social_token` with the provider → extract `provider_user_id` and `provider_email`.
2. Check for existing row with same `(tenant_id, provider, provider_user_id)` — return 409 if already linked.
3. Check for admin-provisioned pending row `(tenant_id, provider, provider_email, provider_user_id IS NULL)` — if found, update it with `provider_user_id` and `linked_on`.
4. If no pending row, insert new `tenant_user_social_identity` row.

---

#### POST /api/v1/auth/refresh

**Purpose:** Exchange a valid refresh token for a new JWT and rotated refresh token.  
**Auth required:** Yes (existing Bearer token in Authorization header).

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| refresh_token | string | yes | Opaque refresh token from prior login or refresh |

**Response (200):**
```json
{ "token": "eyJ...", "expires_in": 3600, "refresh_token": "new-opaque-refresh-token" }
```

**Error cases:** 401 (JWT expired or malformed, or refresh_token not found/expired), 422, 429, 500

**DB access pattern:** No DB access — Redis only.  
**Business logic notes:**
1. Validate Bearer JWT — return 401 if expired or malformed.
2. Look up `refresh_token` in Redis — return 401 if not found or expired.
3. Issue new JWT (stateless re-sign from JWT claims).
4. Rotate refresh token: delete old Redis entry, insert new entry with reset TTL (sliding window — `RefreshTokenLifetimeDays`, default: 7).
5. Return new JWT and new `refresh_token`.

No restriction on how close to expiry the access token must be — refresh can be called at any point while both tokens are valid. Refresh token revocation (logout) is achieved by deleting the Redis entry.

---

### Configuration

---

#### POST /api/v1/tenant-config

**Purpose:** Fetch the tenant/company/branch configuration object. Called at app bootstrap to populate UI settings.  
**Auth required:** Yes.

**Request body:** Standard RequestContext fields only.

**Response (200):**
```json
{
  "tenant_id": "T001", "tenant_name": "Acme Travel Group – London HQ",
  "company_id": "C001", "company_name": "Acme Travel Group",
  "branch_id": "BR001", "branch_name": "London HQ",
  "active_users_inline_threshold": 20,
  "client_logo_url": "https://cdn.example.com/logos/acme.png",
  "client_name": "Acme Travel Group",
  "unclosed_web_enquiries_url": "https://crm.example.com/web-enquiries",
  "task_list_url": "https://crm.example.com/tasks",
  "breadcrumb_position": "inline",
  "footer_gradient_refresh_ms": 300000,
  "ux_version": "1.2.0",
  "api_version": "1.0.0",
  "enabled_auth_methods": ["google", "microsoft", "apple", "magic_link"]
}
```

**Error cases:** 401, 403, 404 (tenant not found), 422, 429, 500

**DB access pattern:** inline-sql  
**Tables:**
| Engine | Full reference |
|---|---|
| MSSQL | `nova_auth.dbo.tenant_config` |
| Postgres | `nova_auth.tenant_config` |
| MariaDB | `` `nova_auth`.`tenant_config` `` |

**Business logic notes:**
- Load single row from `tenant_config` by `(tenant_id, company_id, branch_id)` from RequestContext.
- Return 404 if row not found.
- `ux_version` and `api_version` are returned from `appsettings.json` (deployment artefacts, not runtime DB data — they reflect the deployed API/UX version, not a per-tenant config value).
- All other fields (`client_logo_url`, `client_name`, `active_users_inline_threshold`, etc.) are stored in `tenant_config` and returned as-is.
- `enabled_auth_methods`: stored as comma-separated string in `tenant_config.enabled_auth_methods`. `NULL` in DB → backend returns all four methods: `["google", "microsoft", "apple", "magic_link"]`. UX uses this array to decide which login options to display on the login screen.

---

#### POST /api/v1/novadhruv-mainapp-menus

**Purpose:** Fetch the navigation menu tree for the current user. Items may include `external_url` for CRM/legacy systems.  
**Auth required:** Yes.

**Request body:** Standard RequestContext fields only.

**Response (200):** Array of menu item objects with optional nested `children`.
```json
[
  { "id": "dashboard", "label": "Dashboard", "icon": "layout-dashboard", "route": "/dashboard", "external_url": "", "external_url_param_mode": "none" },
  { "id": "crm-dashboard", "label": "CRM", "icon": "monitor", "external_url": "https://crm.example.com/view", "external_url_param_mode": "query" },
  { "id": "sales", "label": "Sales", "icon": "trending-up",
    "children": [
      { "id": "sales-pipeline", "label": "Pipeline", "route": "/sales/pipeline" },
      { "id": "sales-reports", "label": "Reports", "external_url": "https://reports.example.com/{tenant_id}/user/{user_id}", "external_url_param_mode": "path" }
    ]
  }
]
```

`external_url_param_mode`: `none` | `query` | `path` — controls how tenant/user context is appended to external URLs.

**Error cases:** 401, 403, 404, 422, 429, 500

**DB access pattern:** inline-sql  
**Tables:**
| Engine | Full reference |
|---|---|
| MSSQL | `nova_auth.dbo.tenant_menu_items` |
| Postgres | `nova_auth.tenant_menu_items` |
| MariaDB | `` `nova_auth`.`tenant_menu_items` `` |

**Business logic notes:**
- Load all active menu items for `tenant_id` where `is_active = 1`, ordered by `sort_order`.
- **Role filtering:** Server-side — filter rows where `required_roles` is null/empty (visible to all) OR contains at least one role present in the user's JWT claims. Client receives only items the user can see.
- **Tree assembly:** Server assembles the parent/child tree before returning — items with `parent_id IS NULL` are top-level; children are nested under their parent's `children` array.
- **External URL template resolution:** Server-side — replace `{tenant_id}` and `{user_id}` tokens in `external_url_template` before returning. Client receives a final, ready-to-use URL.
- `external_url_param_mode` (`none` | `query` | `path`) is stored per row and returned as-is — client uses it to know how to append context when navigating.

---

## Database Schema

All tables live in the `nova_auth` database, owned by `Nova.CommonUX.Api`. Column types shown for MSSQL. Postgres uses `timestamptz` for datetime columns and `boolean` for `frz_ind`. MariaDB uses `DATETIME` and `TINYINT(1)`.

---

### nova_auth.dbo.tenant_secrets

Stores one Argon2id-hashed client secret per tenant for machine-to-machine auth.

| Column | Type | Notes |
|---|---|---|
| `tenant_id` | `varchar(50)` | PK |
| `client_secret_hash` | `varchar(500)` | Argon2id hash via Konscious |
| `frz_ind` | `bit` | Default 0 |
| `created_by` | `varchar(50)` | |
| `created_on` | `datetime2` | |
| `updated_by` | `varchar(50)` | |
| `updated_on` | `datetime2` | |
| `updated_at` | `varchar(150)` | Process name or IP address of last writer |

---

### nova_auth.dbo.tenant_user_auth

Stores credentials and auth state per user. One row per `(tenant_id, user_id)`.

| Column | Type | Notes |
|---|---|---|
| `tenant_id` | `varchar(50)` | PK part 1 |
| `user_id` | `varchar(50)` | PK part 2. Human-assigned code e.g. `U001` |
| `password_hash` | `varchar(500)` | Argon2id. Nullable — social-only users have no password |
| `totp_enabled` | `bit` | Default 0 |
| `totp_secret_encrypted` | `varchar(500)` | CipherService encrypted. Nullable until 2FA enrolled |
| `failed_login_count` | `int` | Default 0. Reset on successful login |
| `locked_until` | `datetime2` | Nullable. Account lockout expiry |
| `last_login_on` | `datetime2` | Nullable. Updated on every successful login |
| `frz_ind` | `bit` | Default 0 |
| `created_by` | `varchar(50)` | |
| `created_on` | `datetime2` | |
| `updated_by` | `varchar(50)` | |
| `updated_on` | `datetime2` | |
| `updated_at` | `varchar(150)` | Process name or IP address of last writer |

---

### nova_auth.dbo.tenant_user_profile

Stores identity and display data per user. One row per `(tenant_id, user_id)`.

| Column | Type | Notes |
|---|---|---|
| `tenant_id` | `varchar(50)` | PK part 1 |
| `user_id` | `varchar(50)` | PK part 2 |
| `email` | `varchar(255)` | Unique per tenant. Used for forgot-password and magic-link |
| `display_name` | `varchar(200)` | Returned as `name` in login response |
| `avatar_url` | `varchar(500)` | Nullable |
| `frz_ind` | `bit` | Default 0 |
| `created_by` | `varchar(50)` | |
| `created_on` | `datetime2` | |
| `updated_by` | `varchar(50)` | |
| `updated_on` | `datetime2` | |
| `updated_at` | `varchar(150)` | Process name or IP address of last writer |

---

### nova_auth.dbo.tenant_user_social_identity

Links a Nova user to one or more social provider identities. Supports both admin-provisioned (pending) and user self-linked rows.

| Column | Type | Notes |
|---|---|---|
| `tenant_id` | `varchar(50)` | PK part 1 |
| `user_id` | `varchar(50)` | PK part 2. FK to `tenant_user_auth` |
| `provider` | `varchar(50)` | PK part 3. Values: `google`, `microsoft`, `apple` |
| `provider_user_id` | `varchar(255)` | The `sub` claim from the provider token. Null = pending admin-provisioned link |
| `provider_email` | `varchar(255)` | Email as returned by provider. Used as lookup key for pending links |
| `linked_on` | `datetime2` | Nullable. Populated when link is fully resolved |
| `frz_ind` | `bit` | Default 0 |
| `created_by` | `varchar(50)` | |
| `created_on` | `datetime2` | |
| `updated_by` | `varchar(50)` | |
| `updated_on` | `datetime2` | |
| `updated_at` | `varchar(150)` | Process name or IP address of last writer |

---

### nova_auth.dbo.tenant_auth_tokens

Single-use tokens for password reset and magic link flows. Stores SHA-256 hash — plaintext token is sent to user by email only.

| Column | Type | Notes |
|---|---|---|
| `id` | `uniqueidentifier` | PK. App-generated |
| `tenant_id` | `varchar(50)` | |
| `user_id` | `varchar(50)` | |
| `token_hash` | `varchar(500)` | SHA-256 hash of the plaintext token |
| `token_type` | `varchar(50)` | Values: `password_reset`, `magic_link` |
| `expires_on` | `datetime2` | |
| `used_on` | `datetime2` | Nullable. Populated on use — single-use enforcement |
| `created_on` | `datetime2` | |

---

### nova_auth.dbo.tenant_config

UX and branding configuration per tenant/company/branch. One row per `(tenant_id, company_id, branch_id)`.

| Column | Type | Notes |
|---|---|---|
| `tenant_id` | `varchar(50)` | PK part 1 |
| `company_id` | `varchar(50)` | PK part 2 |
| `branch_id` | `varchar(50)` | PK part 3 |
| `tenant_name` | `varchar(200)` | |
| `company_name` | `varchar(200)` | |
| `branch_name` | `varchar(200)` | |
| `client_name` | `varchar(200)` | Display name for the client |
| `client_logo_url` | `varchar(500)` | Nullable |
| `active_users_inline_threshold` | `int` | Default 20 |
| `unclosed_web_enquiries_url` | `varchar(500)` | Nullable |
| `task_list_url` | `varchar(500)` | Nullable |
| `breadcrumb_position` | `varchar(50)` | e.g. `inline`, `top` |
| `footer_gradient_refresh_ms` | `int` | Default 300000 |
| `enabled_auth_methods` | `varchar(200)` | Nullable. Comma-separated: `google,microsoft,apple,magic_link`. NULL = all enabled |
| `frz_ind` | `bit` | Default 0 |
| `created_by` | `varchar(50)` | |
| `created_on` | `datetime2` | |
| `updated_by` | `varchar(50)` | |
| `updated_on` | `datetime2` | |
| `updated_at` | `varchar(150)` | Process name or IP address of last writer |

---

### nova_auth.dbo.tenant_menu_items

Navigation menu items per tenant. Supports parent/child nesting via `parent_id`. Role filtering via `required_roles`.

| Column | Type | Notes |
|---|---|---|
| `menu_item_id` | `varchar(50)` | PK. Human-assigned code e.g. `dashboard`, `crm-reports` |
| `tenant_id` | `varchar(50)` | |
| `parent_id` | `varchar(50)` | Nullable. FK to `menu_item_id` for nesting |
| `label` | `varchar(200)` | Display label |
| `icon` | `varchar(100)` | Nullable. Icon identifier (e.g. Lucide icon name) |
| `route` | `varchar(500)` | Nullable. Internal SPA route e.g. `/dashboard` |
| `external_url_template` | `varchar(500)` | Nullable. May contain `{tenant_id}`, `{user_id}` tokens |
| `external_url_param_mode` | `varchar(20)` | `none`, `query`, or `path`. Default `none` |
| `required_roles` | `varchar(500)` | Nullable. Comma-separated role codes. Null = visible to all |
| `sort_order` | `int` | Default 0. Controls display order within parent |
| `is_active` | `bit` | Default 1 |
| `frz_ind` | `bit` | Default 0 |
| `created_by` | `varchar(50)` | |
| `created_on` | `datetime2` | |
| `updated_by` | `varchar(50)` | |
| `updated_on` | `datetime2` | |
| `updated_at` | `varchar(150)` | Process name or IP address of last writer |

---

## opsettings.json Reference

All values below are hot-reloadable. Sensitive values (connection strings) are CipherService-encrypted and live in `appsettings.json`, not `opsettings.json`.

```json
{
  "Auth": {
    "FailedLoginMaxAttempts": 5,
    "FailedLoginLockoutMinutes": 15,
    "TwoFaSessionExpiryMinutes": 5,
    "RefreshTokenLifetimeDays": 7,
    "PasswordResetTokenExpiryMinutes": 60,
    "MagicLinkTokenExpiryMinutes": 15
  },
  "Cache": {
    "CacheProvider": "InMemory",
    "InMemoryWarning": "InMemory is suitable for single-instance and local dev only. Redis is required for multi-instance deployments — rate limiting counters and session tokens are not shared across instances with InMemory."
  },
  "Email": {
    "Provider": "SendGrid",
    "SendGrid": {
      "SenderAddress": "noreply@example.com",
      "SenderDisplayName": "Nova Platform"
    }
  },
  "SocialLogin": {
    "Google":    { "ClientId": "...", "ClientSecret": "..." },
    "Microsoft": { "ClientId": "...", "ClientSecret": "..." },
    "Apple":     { "ClientId": "...", "ClientSecret": "..." }
  }
}
```

`appsettings.json` (encrypted, not hot-reloadable):

```json
{
  "Cache": {
    "RedisConnectionString": "<CipherService encrypted>"
  },
  "Email": {
    "SendGrid": {
      "ApiKey": "<CipherService encrypted>"
    }
  }
}
```

| Key | Default | Notes |
|---|---|---|
| `Auth.FailedLoginMaxAttempts` | `5` | Failed attempts before lockout |
| `Auth.FailedLoginLockoutMinutes` | `15` | Lockout duration |
| `Auth.TwoFaSessionExpiryMinutes` | `5` | 2FA session_token TTL in Redis |
| `Auth.RefreshTokenLifetimeDays` | `7` | Sliding window — TTL resets on each use |
| `Auth.PasswordResetTokenExpiryMinutes` | `60` | Expiry for password reset tokens |
| `Auth.MagicLinkTokenExpiryMinutes` | `15` | Expiry for magic link tokens |
| `Cache.CacheProvider` | `InMemory` | `InMemory` or `Redis` |
| `Cache.RedisConnectionString` | — | `appsettings.json` only, CipherService encrypted |
| `Email.Provider` | `SendGrid` | `SendGrid` or `MicrosoftGraph` (Graph impl added later) |
| `Email.SendGrid.ApiKey` | — | `appsettings.json` only, CipherService encrypted |
| `Email.SendGrid.SenderAddress` | — | From address for outbound email |
| `Email.SendGrid.SenderDisplayName` | — | Display name for from address |
| `SocialLogin.*.ClientId` | — | Per-provider OAuth app client ID |
| `SocialLogin.*.ClientSecret` | — | Per-provider OAuth app client secret |
