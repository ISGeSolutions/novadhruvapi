# Nova.CommonUX.Api — Developer Guide

**Port:** 5102  
**Postman collection:** `postman/postman-Nova.CommonUX.Api.json`  
**Postman environment:** `postman/postman-env-dev-Nova.CommonUX.Api.json`

---

## What This Service Is

`Nova.CommonUX.Api` is the authentication gateway for the Nova platform. It owns the `nova_auth` schema and is the only service that issues JWTs. All other services validate tokens against the same secret but never issue them.

| Endpoint | Auth | Purpose |
|---|---|---|
| `POST /api/v1/auth/token` | None | Machine-to-machine token (client secret → JWT) |
| `POST /api/v1/auth/login` | None | User login (password → JWT or 2FA session) |
| `POST /api/v1/auth/verify-2fa` | None | Complete TOTP 2FA (session_token + code → JWT) |
| `POST /api/v1/auth/refresh` | Bearer | Exchange refresh token for new JWT |
| `POST /api/v1/auth/forgot-password` | None | Send password reset email |
| `POST /api/v1/auth/reset-password` | None | Complete password reset with token |
| `POST /api/v1/auth/magic-link` | None | Send magic link login email |
| `POST /api/v1/auth/magic-link/verify` | None | Verify magic link token → JWT |
| `POST /api/v1/auth/social` | None | Initiate OAuth social login |
| `POST /api/v1/auth/social/complete` | None | Complete social login → JWT |
| `POST /api/v1/auth/social/link` | Bearer | Initiate linking social identity to existing account |
| `POST /api/v1/auth/social/link/complete` | Bearer | Complete social identity link |
| `POST /api/v1/tenant-config` | Bearer | UX/branding config for tenant/company/branch |
| `POST /api/v1/novadhruv-mainapp-menus` | Bearer | Role-filtered navigation menu tree |
| `POST /api/v1/hello` | None | Liveness check |
| `POST /run-auth-migrations` | None | Run DbUp migrations (admin, unversioned) |
| `GET /health` | None | ASP.NET Core health endpoint |
| `GET /health/redis` | None | Redis health check |
| `GET /health/db` | None | Database connectivity check |

---

## Configuration Files

| File | Purpose | Reload |
|---|---|---|
| `appsettings.json` | Encrypted connection string, JWT settings, social login credentials | Restart required |
| `opsettings.json` | Auth lockout/TTL settings, cache provider, SQL logging, email | Hot-reload via `IOptionsMonitor` |

### appsettings.json — required sections

```jsonc
{
  "AuthDb": {
    "ConnectionString": "<encrypted>",
    "DbType": "Postgres"           // MsSql | Postgres | MariaDb
  },
  "Jwt": {
    "SecretKey": "<encrypted>",
    "Issuer":    "https://auth.nova.internal",
    "Audience":  "nova-api"
  },
  "Email": {
    "SendGrid": {
      "ApiKey": "<encrypted>"      // omit or leave blank to use NoOpEmailSender
    }
  }
}
```

### opsettings.json — auth and cache settings

```jsonc
{
  "Auth": {
    "FailedLoginMaxAttempts":       5,
    "FailedLoginLockoutMinutes":    15,
    "TwoFaSessionExpiryMinutes":    5,
    "RefreshTokenLifetimeDays":     7,
    "PasswordResetTokenExpiryMinutes": 60,
    "MagicLinkTokenExpiryMinutes":  15
  },
  "Cache": {
    "CacheProvider": "InMemory"    // InMemory (local dev) | Redis (multi-instance)
  },
  "SocialLogin": {
    "Google":    { "ClientId": "", "ClientSecret": "" },
    "Microsoft": { "ClientId": "", "ClientSecret": "" },
    "Apple":     { "ClientId": "", "ClientSecret": "" }
  }
}
```

---

## Session Store — InMemory vs Redis

2FA session tokens and refresh tokens are stored in a session store resolved at startup:

- `Cache:CacheProvider = InMemory` → `InMemorySessionStore` — single instance only, not suitable for multi-pod deployments. Fine for local dev.
- `Cache:CacheProvider = Redis` → `RedisSessionStore` — requires Redis. The Aspire AppHost wires up Redis via `builder.AddRedisClient("redis")`.

For local development, `InMemory` is the default and requires no additional infrastructure.

---

## Email — SendGrid / NoOp

The forgot-password and magic-link flows send emails via `IEmailSender`. The implementation is resolved at startup:

- If `Email:SendGrid:ApiKey` is set → `SendGridEmailSender`
- If absent or empty → `NoOpEmailSender` — logs the email as a `Warning` with the full body including tokens

`NoOpEmailSender` is the correct choice for local development without a SendGrid key. Check the Serilog structured log for the `password_reset_token` or `magic_link_token` to complete flows manually in Postman.

---

## Database Migrations

Migrations run manually via the admin endpoint (not on startup):

```
POST http://localhost:5102/run-auth-migrations
```

Migration files:
```
src/services/Nova.CommonUX.Api/Migrations/
  MsSql/    V001__CreateNovaAuth.sql
            V002__AddEnabledAuthMethods.sql
            V003__AddMustChangePassword.sql
  Postgres/ V001__CreateNovaAuth.sql
            V002__AddEnabledAuthMethods.sql
            V003__AddMustChangePassword.sql
  MariaDb/  V001__CreateNovaAuth.sql
            V002__AddEnabledAuthMethods.sql
            V003__AddMustChangePassword.sql
```

### Schema — nova_auth

| Table | Purpose |
|---|---|
| `tenant_secrets` | Machine-to-machine client secret hashes (one row per tenant) |
| `tenant_user_auth` | User credentials: password hash, TOTP, lockout state |
| `tenant_user_profile` | User display name, email, avatar URL, `program_id_root` |
| `tenant_user_social_identity` | Linked OAuth social identities per user per provider |
| `tenant_auth_tokens` | Immutable log of password-reset and magic-link tokens |
| `tenant_config` | UX/branding config per tenant/company/branch |

V002 adds `enabled_auth_methods` to `tenant_config`. V003 adds `must_change_password` to `tenant_user_auth`. V004 adds `program_id_root` to `tenant_user_profile`.

---

## Seeding Required Data Before Testing

The database starts empty after migrations. Two tables need seed data to exercise the main flows.

### 1. tenant_secrets — required for /auth/token

`/auth/token` verifies a client secret using Argon2id. The hash must be seeded manually.

**Step 1 — generate the hash using Nova.Cipher:**

```bash
dotnet run --project src/tools/Nova.Cipher -- argon2 your-chosen-secret
# Output: argon2id:65536:3:1:<salt>:<hash>
```

**Step 2 — verify the hash before inserting:**

```bash
dotnet run --project src/tools/Nova.Cipher -- verify "your-chosen-secret" "argon2id:65536:3:1:<salt>:<hash>"
# Must output: MATCH — plaintext is correct.
```

**Step 3 — insert into the database:**

MSSQL:
```sql
INSERT INTO nova_auth.dbo.tenant_secrets
  (tenant_id, client_secret_hash, frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
VALUES
  ('BTDK', 'argon2id:65536:3:1:<salt>:<hash>', 0, 'SYS', GETUTCDATE(), 'SYS', GETUTCDATE(), 'Nova.Cipher');
```

Postgres:
```sql
INSERT INTO nova_auth.tenant_secrets
  (tenant_id, client_secret_hash, frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
VALUES
  ('BTDK', 'argon2id:65536:3:1:<salt>:<hash>', false, 'SYS', now(), 'SYS', now(), '');
```

If you need to update an existing row (e.g. after changing the secret):

MSSQL:
```sql
UPDATE nova_auth.dbo.tenant_secrets
SET client_secret_hash = 'argon2id:65536:3:1:<salt>:<hash>',
    updated_by = 'SYS', updated_on = GETUTCDATE(), updated_at = 'Nova.Cipher'
WHERE tenant_id = 'BTDK';
```

**Step 3 — Postman request body:**

```json
{
  "tenant_id": "BTDK",
  "client_secret": "your-chosen-secret"
}
```

On success the endpoint returns `{ "token": "eyJ...", "expires_in": 3600 }`. The Postman post-response script saves this automatically to `{{access_token}}`.

### 2. tenant_user_auth + tenant_user_profile — required for /auth/login

User credentials also use Argon2id. Generate the password hash the same way:

```bash
dotnet run --project src/tools/Nova.Cipher -- argon2 MyPassword123!
# Output: argon2id:65536:3:1:<salt>:<hash>
```

To confirm the hash is correct before inserting:

```bash
dotnet run --project src/tools/Nova.Cipher -- verify "MyPassword123!" "argon2id:65536:3:1:<salt>:<hash>"
# Output: MATCH — plaintext is correct.
```

**MSSQL:**

```sql
INSERT INTO nova_auth.dbo.tenant_user_auth
  (tenant_id, user_id, password_hash, totp_enabled, failed_login_count,
   must_change_password, frz_ind,
   created_by, created_on, updated_by, updated_on, updated_at)
VALUES
  ('BTDK', 'ISG', 'argon2id:65536:3:1:<salt>:<hash>', 0, 0,
   0, 0,
   'SYS', GETUTCDATE(), 'SYS', GETUTCDATE(), 'Nova.Cipher');

INSERT INTO nova_auth.dbo.tenant_user_profile
  (tenant_id, user_id, email, display_name,
   frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
VALUES
  ('BTDK', 'ISG', 'dev@example.com', 'Dev User',
   0, 'SYS', GETUTCDATE(), 'SYS', GETUTCDATE(), 'Nova.Cipher');
```

**Postgres:**

```sql
INSERT INTO nova_auth.tenant_user_auth
  (tenant_id, user_id, password_hash, totp_enabled, failed_login_count,
   must_change_password, frz_ind,
   created_by, created_on, updated_by, updated_on, updated_at)
VALUES
  ('BTDK', 'ISG', 'argon2id:65536:3:1:<salt>:<hash>', false, 0,
   false, false,
   'SYS', now(), 'SYS', now(), '');

INSERT INTO nova_auth.tenant_user_profile
  (tenant_id, user_id, email, display_name,
   frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
VALUES
  ('BTDK', 'ISG', 'dev@example.com', 'Dev User',
   false, 'SYS', now(), 'SYS', now(), '');
```

**Postman request body:**

```json
{
  "tenant_id": "BTDK",
  "user_id":   "ISG",
  "password":  "MyPassword123!"
}
```

**Login rules enforced by the endpoint:**

| Rule | Detail |
|---|---|
| Password minimum length | 8 characters — shorter passwords return 422 |
| Wrong password lockout | After `FailedLoginMaxAttempts` (default: 5) failed attempts the account locks for `FailedLoginLockoutMinutes` (default: 15 min) |
| Frozen account | `frz_ind = 1` returns 401 silently — indistinguishable from wrong password |
| TOTP enabled | If `totp_enabled = 1`, response returns `requires_2fa: true` and a `session_token` instead of a JWT — caller must then POST to `/auth/verify-2fa` |
| must_change_password | If `must_change_password = 1`, a JWT is still issued — the frontend is responsible for enforcing the password change flow |
| Successful login | Resets `failed_login_count = 0`, clears `locked_until`, updates `last_login_on`, issues JWT + refresh token |

**On success the endpoint returns:**

```json
{
  "token":        "eyJ...",
  "expires_in":   3600,
  "requires_2fa": false,
  "refresh_token": "opaque-token",
  "user": {
    "user_id":    "ISG",
    "name":       "Dev User",
    "email":      "dev@example.com",
    "avatar_url": null
  }
}
```

### 3. tenant_config — required for /tenant-config

**MSSQL:**

```sql
INSERT INTO nova_auth.dbo.tenant_config
  (tenant_id, company_code, branch_code, tenant_name, company_name, branch_name,
   client_name, client_logo_url, 
   active_users_inline_threshold, unclosed_web_enquiries_url, task_list_url,
   breadcrumb_position, footer_gradient_refresh_ms, enabled_auth_methods,
   frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
VALUES
  ('BTDK', 'BTDK', 'DK', 'Blixen Tours', 'Blixen Tour (c)', 'Denmark',
   'Blixen Tours', 'https://www.blixentours.dk/dist/images/svg/blixen-logo.svg',
   20, 'https://crm.example.com/web-enquiries', 'https://crm.example.com/tasks',
   'inline', 300000, 'google, microsoft, apple,magic_link',
   false, 'SYS', now(), 'SYS', now(), 'PRESET');
```

### 4. tenant_user_auth — enable TOTP for BTDK/ISG (required for /auth/verify-2fa)

The row must already exist (seeded in step 2 above). These statements enable TOTP using a **known test secret** so any TOTP app can generate the correct codes.

**Step 1 — add the secret to your TOTP app**

Open Google Authenticator, Authy, or any RFC 6238 compatible app and add an entry manually:

| Field | Value |
|---|---|
| Account name | `BTDK / ISG (Nova dev)` |
| Secret (Base32) | `JBSWY3DPEHPK3PXP` |
| Algorithm | SHA1 (default) |
| Digits | 6 (default) |
| Period | 30 seconds (default) |

**Step 2 — run the seed SQL (one dialect only — whichever your dev DB is)**

MSSQL:
```sql
UPDATE nova_auth.dbo.tenant_user_auth
SET    totp_enabled          = 1,
       totp_secret_encrypted = 'kvMz9ZiRGtQNj7RVt3OUiCUAon5OkZs15NVdPf3gCpg0x5DWIYjKtnyKXfnWIAMt',
       updated_by            = 'SYS',
       updated_on            = GETUTCDATE(),
       updated_at            = 'seed'
WHERE  tenant_id = 'BTDK'
AND    user_id   = 'ISG';
```

Postgres:
```sql
UPDATE nova_auth.tenant_user_auth
SET    totp_enabled          = true,
       totp_secret_encrypted = 'kvMz9ZiRGtQNj7RVt3OUiCUAon5OkZs15NVdPf3gCpg0x5DWIYjKtnyKXfnWIAMt',
       updated_by            = 'SYS',
       updated_on            = now(),
       updated_at            = 'seed'
WHERE  tenant_id = 'BTDK'
AND    user_id   = 'ISG';
```

MariaDB:
```sql
UPDATE nova_auth.tenant_user_auth
SET    totp_enabled          = 1,
       totp_secret_encrypted = 'kvMz9ZiRGtQNj7RVt3OUiCUAon5OkZs15NVdPf3gCpg0x5DWIYjKtnyKXfnWIAMt',
       updated_by            = 'SYS',
       updated_on            = UTC_TIMESTAMP(),
       updated_at            = 'seed'
WHERE  tenant_id = 'BTDK'
AND    user_id   = 'ISG';
```

The encrypted value above was produced with:
```bash
ENCRYPTION_KEY="customersatisfactionthroughtechnicalexcellence" \
  ./src/tools/Nova.Cipher/bin/Release/net10.0/nova-cipher encrypt "JBSWY3DPEHPK3PXP"
```

**Step 3 — Postman test flow**

1. **POST /api/v1/auth/login** — body `{ "tenant_id": "BTDK", "user_id": "ISG", "password": "MyPassword123!" }`.  
   Response will be `{ "requires_2fa": true, "session_token": "..." }` — no JWT is issued yet.  
   The Tests script captures `session_token` automatically to `{{session_token}}`.

2. **POST /api/v1/auth/verify-2fa** — body `{ "session_token": "{{session_token}}", "code": "<6-digit code from TOTP app>" }`.  
   Response is the full login response with JWT + refresh token.

Note: the 2FA session expires after `Auth:TwoFaSessionExpiryMinutes` (default 5 min). If the session expires, repeat from step 1.

**To disable TOTP again** (restore to password-only login):

MSSQL: `UPDATE nova_auth.dbo.tenant_user_auth SET totp_enabled = 0, updated_by = 'SYS', updated_on = GETUTCDATE(), updated_at = 'seed' WHERE tenant_id = 'BTDK' AND user_id = 'ISG';`

Postgres: `UPDATE nova_auth.tenant_user_auth SET totp_enabled = false, updated_by = 'SYS', updated_on = now(), updated_at = 'seed' WHERE tenant_id = 'BTDK' AND user_id = 'ISG';`

---

## Postman — Auto JWT

The collection pre-request script auto-generates an HS256 JWT before every authenticated request. No manual token management is needed.

**Required variables** (set in `postman-env-dev-Nova.CommonUX.Api.json` or Postman Globals):

| Variable | Where to get it |
|---|---|
| `jwt_secret` | Decrypt `Jwt:SecretKey` from `appsettings.json` using `nova-cipher decrypt <value>` — set in **Globals** |
| `tenant_id` | `BTDK` (default in dev env) |
| `company_code` | `BTDK` (default in dev env) |
| `branch_code` | `DK` (default in dev env) |
| `user_id` | `ISG` (default in dev env) |

The script skips token generation for anonymous endpoints (`/auth/login`, `/auth/token`, `/health`, etc.) and reuses the existing token if it has more than 60 seconds of remaining lifetime.

**Anonymous endpoints** — no Bearer token is generated or sent:
- `/hello`, `/health`, `/health/redis`
- `/auth/token`, `/auth/login`, `/auth/forgot-password`, `/auth/reset-password`
- `/auth/magic-link`, `/auth/verify-2fa`
- `/auth/social` (initiate + complete) — but NOT `/auth/social/link` or `/auth/social/link/complete`

---

## Nova.Cipher — argon2 command

`Nova.Cipher` is the dev tooling CLI for Nova. It supports four commands:

```bash
# Encrypt a plaintext config value (requires ENCRYPTION_KEY env var)
dotnet run --project src/tools/Nova.Cipher -- encrypt "my-secret"

# Decrypt an encrypted config value (requires ENCRYPTION_KEY env var)
dotnet run --project src/tools/Nova.Cipher -- decrypt "ENC:..."

# Hash a plaintext value using Argon2id (no ENCRYPTION_KEY needed)
dotnet run --project src/tools/Nova.Cipher -- argon2 "my-secret"

# Verify a plaintext against a stored Argon2id hash (no ENCRYPTION_KEY needed)
# Exit code 0 = match, 1 = no match
dotnet run --project src/tools/Nova.Cipher -- verify "my-secret" "argon2id:65536:3:1:<salt>:<hash>"
```

Use `verify` to confirm a hash in the database is correct before testing an endpoint — this saves a round-trip to the API and avoids triggering the failed-login lockout counter.

The `argon2` output uses the same parameters as `Argon2idHasher` in `Nova.CommonUX.Api.Services`:
- Memory: 64 MB (`65536` KB)
- Iterations: 3
- Parallelism: 1
- Hash length: 32 bytes

Output format: `argon2id:65536:3:1:<base64-salt>:<base64-hash>`

---

## Navigation Menus

`/novadhruv-mainapp-menus` builds the navigation tree for the authenticated user. Menu data lives in `Nova.Presets.Api` (`presets.programs` + `presets.program_tree`); access control is via `program_id_root` on the user's profile.

- **`program_id_root`** — set on `nova_auth.tenant_user_profile` at user provisioning time. The menus endpoint reads this value and returns only the subtree rooted at that program. No runtime role filtering — access changes are handled by updating `program_id_root`.
- **`nav_type`** — discriminator on each program: `'group'` (header, non-navigating), `'internal'` (React Router `route`), `'external'` (`external_url` opened in a new tab).
- **Contract rule** — exactly one of `route` or `external_url` is non-null per non-group item.
- Tree assembly is performed in C# after fetching all active programs and tree edges. Orphaned subtrees (parent node not in active set) are silently dropped.

To seed programs data for testing, see `src/services/Nova.Presets.Api/docs/nova-presets-api-guide.md` — programs and program_tree are owned by Presets.Api (V003 migration).
