# Nova.Presets.Api — SQL Query Review

**Purpose:** Reference for the API team writing MSSQL versions of every query used by
`Nova.Presets.Api`. Each entry lists the source file, method, endpoint, tables touched,
and the full SQL for Postgres / MariaDB so the MSSQL equivalent can be written with
correct aliases.

---

## Table naming by dialect

| Dialect | Format | Example |
|---|---|---|
| Postgres | `schema.table_name` | `presets.branch` |
| MariaDB/MySQL | `` `database`.`table_name` `` | `` `presets`.`branch` `` |
| MSSQL (new tables) | `[database].schema.table_name` | `[yourdb].presets.branch` |
| MSSQL (legacy tables) | `[actual_legacy_db].dbo.TableName` | **see note below** |

> **MSSQL database name:** The code uses the database name written in `appsettings.json`
> `PresetsDb:ConnectionString` and `AuthDb:ConnectionString`. The schema component (`presets`,
> `nova_auth`) is fixed. Replace `[yourdb]` with the actual database name for the environment.

> **MSSQL-LEGACY tables (`Branch`, `Company`):** These are legacy PascalCase tables.
> The code currently references `presets.dbo.Branch` / `presets.dbo.Company` — this is a
> placeholder database name. The actual database these tables live in must be confirmed
> per-tenant and the reference updated manually. Column names are PascalCase and use
> `ISNULL(col, 0)` null-guards on nullable `bit` columns.

---

## Databases and schemas

| Setting key | Schema | Managed by |
|---|---|---|
| `AuthDb` | `nova_auth` | `Nova.CommonUX.Api` migrations |
| `PresetsDb` | `presets` | `Nova.Presets.Api` migrations (V001–V005) |

All `nova_auth` and `presets` tables are **new** (snake_case columns, UUID PKs) **except**
the legacy `Branch` and `Company` tables accessed via `PresetsDb` in the
`BranchesEndpoint` MSSQL path.

---

## Query catalogue

---

### Q01 — Fetch active branches (SELECT)

| | |
|---|---|
| **File** | `Endpoints/PresetsDbHelper.cs` |
| **Method** | `BranchesQuery(PresetsDbSettings)` |
| **Endpoint** | `POST /api/v1/branches` |
| **Database** | PresetsDb |
| **Tables** | `presets.branch`, `presets.company` |
| **Operation** | SELECT |

**Intent:** Return all active branches with their parent company names for a tenant, ordered
by branch name. Frontend uses this to populate branch selectors.

**Postgres / MariaDB:**
```sql
SELECT br.branch_code  AS BranchCode,
       br.branch_name  AS BranchName,
       co.company_code AS CompanyCode,
       co.company_name AS CompanyName
FROM   presets.branch  br
INNER  JOIN presets.company co ON co.company_code = br.company_code
WHERE  co.tenant_id = @TenantId
AND    br.frz_ind   = false
AND    co.frz_ind   = false
ORDER  BY br.branch_name
```

**MSSQL (LEGACY path):**
```sql
-- MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by rajeevjha on 14 Apr 2026.
SELECT br.BranchCode,
       br.BranchName,
       co.CompanyCode,
       co.CompanyName
FROM   presets.dbo.Branch  br        -- ← replace 'presets' with actual legacy database name
INNER  JOIN presets.dbo.Company co ON co.CompanyCode = br.CompanyCode
WHERE  co.TenantId         = @TenantId
AND    ISNULL(br.FrzInd,0) = 0
AND    ISNULL(co.FrzInd,0) = 0
ORDER  BY br.BranchName
```

**Dapper result type:** `BranchRow(BranchCode, BranchName, CompanyCode, CompanyName)` —
all four aliases must match these property names exactly.

---

### Q02 — Fetch user profile (SELECT)

| | |
|---|---|
| **File** | `Endpoints/UserProfileEndpoint.cs` |
| **Method** | `HandleAsync(...)` — Query 1 |
| **Endpoint** | `POST /api/v1/user-profile` |
| **Database** | AuthDb |
| **Tables** | `nova_auth.tenant_user_profile` |
| **Operation** | SELECT single row |

**Intent:** Retrieve display name, email address, avatar URL, and program root ID for the
requested user. Returns 404 if no active profile exists.

```sql
SELECT display_name    AS DisplayName,
       email           AS Email,
       avatar_url      AS AvatarUrl,
       program_id_root AS ProgramIdRoot
FROM   nova_auth.tenant_user_profile
WHERE  tenant_id = @TenantId
AND    user_id   = @UserId
AND    frz_ind   = false
```

---

### Q03 — Fetch user presence status (SELECT)

| | |
|---|---|
| **File** | `Endpoints/UserProfileEndpoint.cs` |
| **Method** | `HandleAsync(...)` — Query 2a |
| **Endpoint** | `POST /api/v1/user-profile` |
| **Database** | PresetsDb |
| **Tables** | `presets.tenant_user_status` |
| **Operation** | SELECT single row |

**Intent:** Retrieve the user's current presence status (id, label, optional note).
Returns null if the user has never set a status; the endpoint then defaults to
`available / Available`.

```sql
SELECT status_id    AS StatusId,
       status_label AS StatusLabel,
       status_note  AS StatusNote
FROM   presets.tenant_user_status
WHERE  tenant_id = @TenantId
AND    user_id   = @UserId
```

---

### Q04 — Fetch user permissions (SELECT)

| | |
|---|---|
| **File** | `Endpoints/PresetsDbHelper.cs` |
| **Method** | `UserPermissionsQuerySql(PresetsDbSettings)` |
| **Endpoint** | `POST /api/v1/user-profile` |
| **Database** | PresetsDb |
| **Tables** | `presets.tenant_user_permissions` |
| **Operation** | SELECT list |

**Intent:** Return all permission codes assigned to the user. Returned as a `permissions[]`
array in the profile response — used by the frontend to show/hide UI affordances.
No `frz_ind` on this table; rows are deleted rather than frozen.

```sql
SELECT permission_code AS PermissionCode
FROM   presets.tenant_user_permissions
WHERE  tenant_id = @TenantId
AND    user_id   = @UserId
```

---

### Q05 — List status options (SELECT with tier resolution)

| | |
|---|---|
| **File** | `Endpoints/PresetsDbHelper.cs` |
| **Method** | `StatusOptionsQuerySql(PresetsDbSettings)` |
| **Endpoint** | `POST /api/v1/user-profile/status-options` |
| **Database** | PresetsDb |
| **Tables** | `presets.user_status_options` |
| **Operation** | SELECT list (CTE + ROW_NUMBER) |

**Intent:** Return the resolved list of presence status options for a tenant/company/branch.
Most-specific-tier wins: branch-level (3) beats company-level (2) beats tenant-level (1).
When the same `status_code` appears at multiple tiers, only the most specific row is kept.
Results are ordered by `serial_no` then `label`.

```sql
WITH ranked AS (
    SELECT status_code AS StatusCode,
           label       AS Label,
           colour      AS Colour,
           serial_no,
           ROW_NUMBER() OVER (
               PARTITION BY status_code
               ORDER BY
                   CASE
                       WHEN company_code <> 'XXXX' AND branch_code <> 'XXXX' THEN 3
                       WHEN company_code <> 'XXXX'                            THEN 2
                       ELSE                                                        1
                   END DESC
           ) AS rn
    FROM   presets.user_status_options
    WHERE  tenant_id = @TenantId
    AND    frz_ind   = false
    AND    (
               (company_code = 'XXXX'       AND branch_code = 'XXXX')
            OR (company_code = @CompanyCode AND branch_code = 'XXXX')
            OR (company_code = @CompanyCode AND branch_code = @BranchCode)
           )
)
SELECT StatusCode, Label, Colour
FROM   ranked
WHERE  rn = 1
ORDER  BY serial_no, Label
```

---

### Q06 — Validate a single status option (SELECT with tier resolution)

| | |
|---|---|
| **File** | `Endpoints/PresetsDbHelper.cs` |
| **Method** | `FindStatusOptionSql(PresetsDbSettings)` |
| **Endpoint** | `PATCH /api/v1/user-profile/status` |
| **Database** | PresetsDb |
| **Tables** | `presets.user_status_options` |
| **Operation** | SELECT single row (CTE + ROW_NUMBER) |

**Intent:** Look up a specific `status_code` and confirm it is a valid active option for
the caller's tenant/company/branch (same tier-resolution logic as Q05). Returns null if
the code is unknown or frozen at all applicable tiers.

```sql
WITH ranked AS (
    SELECT status_code AS StatusCode,
           label       AS Label,
           colour      AS Colour,
           ROW_NUMBER() OVER (
               PARTITION BY status_code
               ORDER BY
                   CASE
                       WHEN company_code <> 'XXXX' AND branch_code <> 'XXXX' THEN 3
                       WHEN company_code <> 'XXXX'                            THEN 2
                       ELSE                                                        1
                   END DESC
           ) AS rn
    FROM   presets.user_status_options
    WHERE  tenant_id   = @TenantId
    AND    status_code = @StatusCode
    AND    frz_ind     = false
    AND    (
               (company_code = 'XXXX'       AND branch_code = 'XXXX')
            OR (company_code = @CompanyCode AND branch_code = 'XXXX')
            OR (company_code = @CompanyCode AND branch_code = @BranchCode)
           )
)
SELECT StatusCode, Label, Colour
FROM   ranked
WHERE  rn = 1
```

---

### Q07 — Upsert user presence status (UPSERT)

| | |
|---|---|
| **File** | `Endpoints/PresetsDbHelper.cs` |
| **Method** | `StatusUpsertSql(PresetsDbSettings)` |
| **Endpoint** | `PATCH /api/v1/user-profile/status` |
| **Database** | PresetsDb |
| **Tables** | `presets.tenant_user_status` |
| **Operation** | INSERT … ON CONFLICT UPDATE (Postgres) / ON DUPLICATE KEY UPDATE (MariaDB) / MERGE (MSSQL) |

**Intent:** Write the user's current presence status. Creates the row on first status set;
updates it on subsequent changes. PK is `(tenant_id, user_id)`.

**Postgres:**
```sql
INSERT INTO presets.tenant_user_status
    (tenant_id, user_id, status_id, status_label, status_note,
     frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
VALUES
    (@TenantId, @UserId, @StatusId, @StatusLabel, @StatusNote,
     false, 'system', @Now, 'system', @Now, 'Nova.Presets.Api')
ON CONFLICT (tenant_id, user_id) DO UPDATE SET
    status_id    = EXCLUDED.status_id,
    status_label = EXCLUDED.status_label,
    status_note  = EXCLUDED.status_note,
    updated_on   = EXCLUDED.updated_on,
    updated_by   = EXCLUDED.updated_by,
    updated_at   = EXCLUDED.updated_at
```

**MariaDB:**
```sql
INSERT INTO `presets`.`tenant_user_status`
    (`tenant_id`, `user_id`, `status_id`, `status_label`, `status_note`,
     `frz_ind`, `created_by`, `created_on`, `updated_by`, `updated_on`, `updated_at`)
VALUES
    (@TenantId, @UserId, @StatusId, @StatusLabel, @StatusNote,
     0, 'system', @Now, 'system', @Now, 'Nova.Presets.Api')
ON DUPLICATE KEY UPDATE
    `status_id`    = VALUES(`status_id`),
    `status_label` = VALUES(`status_label`),
    `status_note`  = VALUES(`status_note`),
    `updated_on`   = VALUES(`updated_on`),
    `updated_by`   = VALUES(`updated_by`),
    `updated_at`   = VALUES(`updated_at`)
```

**MSSQL:**
```sql
MERGE INTO [yourdb].presets.tenant_user_status WITH (HOLDLOCK) AS target
USING (SELECT @TenantId AS tenant_id, @UserId AS user_id) AS source
      ON target.tenant_id = source.tenant_id AND target.user_id = source.user_id
WHEN MATCHED THEN
    UPDATE SET status_id    = @StatusId,
               status_label = @StatusLabel,
               status_note  = @StatusNote,
               updated_on   = @Now,
               updated_by   = 'system',
               updated_at   = 'Nova.Presets.Api'
WHEN NOT MATCHED THEN
    INSERT (tenant_id, user_id, status_id, status_label, status_note,
            frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
    VALUES (@TenantId, @UserId, @StatusId, @StatusLabel, @StatusNote,
            0, 'system', @Now, 'system', @Now, 'Nova.Presets.Api');
```

---

### Q08 — Re-fetch profile after status update (SELECT)

| | |
|---|---|
| **File** | `Endpoints/UpdateStatusEndpoint.cs` |
| **Method** | `HandleAsync(...)` |
| **Endpoint** | `PATCH /api/v1/user-profile/status` |
| **Database** | AuthDb |
| **Tables** | `nova_auth.tenant_user_profile` |
| **Operation** | SELECT single row |

**Intent:** Re-read the user's profile from AuthDb so the 200 response returns the
canonical name, email, and avatar URL (these fields are not in PresetsDb).

```sql
SELECT display_name AS DisplayName,
       email        AS Email,
       avatar_url   AS AvatarUrl
FROM   nova_auth.tenant_user_profile
WHERE  tenant_id = @TenantId
AND    user_id   = @UserId
AND    frz_ind   = false
```

---

### Q09 — Verify current password (SELECT)

| | |
|---|---|
| **File** | `Endpoints/ChangePasswordEndpoint.cs` |
| **Method** | `HandleAsync(...)` — Step 1 |
| **Endpoint** | `POST /api/v1/user-profile/change-password` |
| **Database** | AuthDb |
| **Tables** | `nova_auth.tenant_user_auth` |
| **Operation** | SELECT single row |

**Intent:** Retrieve the stored Argon2id password hash so the caller's current password
can be verified before allowing a change request.

```sql
SELECT user_id        AS UserId,
       password_hash  AS PasswordHash
FROM   nova_auth.tenant_user_auth
WHERE  tenant_id = @TenantId
AND    user_id   = @UserId
AND    frz_ind   = false
```

---

### Q10 — Fetch email for confirmation message (SELECT scalar)

| | |
|---|---|
| **File** | `Endpoints/ChangePasswordEndpoint.cs` |
| **Method** | `HandleAsync(...)` — Step 2 |
| **Endpoint** | `POST /api/v1/user-profile/change-password` |
| **Database** | AuthDb |
| **Tables** | `nova_auth.tenant_user_profile` |
| **Operation** | SELECT scalar |

**Intent:** Retrieve the user's email address so the confirmation link can be sent.
Runs on the same AuthDb connection as Q09 to avoid a second round-trip.

```sql
SELECT email
FROM   nova_auth.tenant_user_profile
WHERE  tenant_id = @TenantId
AND    user_id   = @UserId
```

---

### Q11 — Clear pending password change requests (DELETE)

| | |
|---|---|
| **File** | `Endpoints/ChangePasswordEndpoint.cs` |
| **Method** | `HandleAsync(...)` — Step 4a |
| **Endpoint** | `POST /api/v1/user-profile/change-password` |
| **Database** | PresetsDb |
| **Tables** | `presets.tenant_password_change_requests` |
| **Operation** | DELETE |

**Intent:** Remove any unconfirmed pending requests for the user before inserting a new
one. Prevents accumulation of stale tokens. Only unconfirmed rows are deleted
(`confirmed_on IS NULL`).

```sql
DELETE FROM presets.tenant_password_change_requests
WHERE tenant_id    = @TenantId
AND   user_id      = @UserId
AND   confirmed_on IS NULL
```

---

### Q12 — Insert password change request (INSERT)

| | |
|---|---|
| **File** | `Endpoints/ChangePasswordEndpoint.cs` |
| **Method** | `HandleAsync(...)` — Step 4b |
| **Endpoint** | `POST /api/v1/user-profile/change-password` |
| **Database** | PresetsDb |
| **Tables** | `presets.tenant_password_change_requests` |
| **Operation** | INSERT |

**Intent:** Store the new password hash, a SHA-256 hash of the confirmation token, and
an expiry timestamp. The plaintext token is emailed to the user; only the hash is stored.
`confirmed_on` starts NULL and is set by Q15 when the user confirms.

```sql
INSERT INTO presets.tenant_password_change_requests
    (id, tenant_id, user_id, new_password_hash, token_hash, expires_on, created_on)
VALUES
    (@Id, @TenantId, @UserId, @NewPasswordHash, @TokenHash, @ExpiresOn, @Now)
```

Parameters: `@Id` = UUID v7 (app-generated), `@ExpiresOn` = `@Now + TokenExpiryMinutes`.

---

### Q13 — Find valid pending request by token hash (SELECT)

| | |
|---|---|
| **File** | `Endpoints/ConfirmPasswordChangeEndpoint.cs` |
| **Method** | `HandleAsync(...)` — Step 1 |
| **Endpoint** | `POST /api/v1/user-profile/confirm-password-change` |
| **Database** | PresetsDb |
| **Tables** | `presets.tenant_password_change_requests` |
| **Operation** | SELECT single row |

**Intent:** Look up an unexpired, unconfirmed request by matching the SHA-256 hash of the
submitted token. Returns null if the token is unknown, already used, or expired; the
endpoint then returns 400.

```sql
SELECT id,
       tenant_id          AS TenantId,
       user_id            AS UserId,
       new_password_hash  AS NewPasswordHash
FROM   presets.tenant_password_change_requests
WHERE  token_hash   = @TokenHash
AND    confirmed_on IS NULL
AND    expires_on   > @Now
```

---

### Q14 — Apply new password to auth record (UPDATE)

| | |
|---|---|
| **File** | `Endpoints/ConfirmPasswordChangeEndpoint.cs` |
| **Method** | `HandleAsync(...)` — Step 2 |
| **Endpoint** | `POST /api/v1/user-profile/confirm-password-change` |
| **Database** | AuthDb |
| **Tables** | `nova_auth.tenant_user_auth` |
| **Operation** | UPDATE |

**Intent:** Write the new Argon2id password hash and clear `must_change_password`.
This is the commit point — after this the user can log in with the new password.

```sql
UPDATE nova_auth.tenant_user_auth
SET    password_hash        = @Hash,
       must_change_password = false,
       updated_on           = @Now,
       updated_by           = 'Auto',
       updated_at           = 'Nova.Presets.Api'
WHERE  tenant_id = @TenantId
AND    user_id   = @UserId
```

---

### Q15 — Mark password change request as confirmed (UPDATE)

| | |
|---|---|
| **File** | `Endpoints/ConfirmPasswordChangeEndpoint.cs` |
| **Method** | `HandleAsync(...)` — Step 3 |
| **Endpoint** | `POST /api/v1/user-profile/confirm-password-change` |
| **Database** | PresetsDb |
| **Tables** | `presets.tenant_password_change_requests` |
| **Operation** | UPDATE |

**Intent:** Stamp `confirmed_on` on the request row so the token cannot be replayed.
Runs after Q14 succeeds. Matched by PK (`id`), not token hash.

```sql
UPDATE presets.tenant_password_change_requests
SET    confirmed_on = @Now
WHERE  id = @Id
```

---

### Q16 — Check target user profile exists (SELECT scalar)

| | |
|---|---|
| **File** | `Endpoints/DefaultPasswordEndpoint.cs` |
| **Method** | `HandleAsync(...)` |
| **Endpoint** | `POST /api/v1/user/default-password` |
| **Database** | AuthDb |
| **Tables** | `nova_auth.tenant_user_profile` |
| **Operation** | SELECT scalar (existence check) |

**Intent:** Confirm the target user has an active profile before creating or overwriting
their auth row. Prevents orphan `tenant_user_auth` rows for users who no longer exist.
Returns the boolean literal `true` if found, so the scalar maps to `bool profileExists`.

**Postgres / MariaDB:**
```sql
SELECT true
FROM   nova_auth.tenant_user_profile
WHERE  tenant_id = @TenantId
AND    user_id   = @TargetUserId
AND    frz_ind   = false
```

**MSSQL:**
```sql
SELECT 1
FROM   nova_auth.tenant_user_profile
WHERE  tenant_id = @TenantId
AND    user_id   = @TargetUserId
AND    frz_ind   = 0
```

---

### Q17 — Upsert default password (UPSERT)

| | |
|---|---|
| **File** | `Endpoints/PresetsDbHelper.cs` |
| **Method** | `DefaultPasswordUpsertSql(DbType)` |
| **Endpoint** | `POST /api/v1/user/default-password` |
| **Database** | AuthDb |
| **Tables** | `nova_auth.tenant_user_auth` |
| **Operation** | INSERT … ON CONFLICT UPDATE / ON DUPLICATE KEY UPDATE / MERGE |

**Intent:** Set or create the auth row for the target user with a default password
(`changeMe@ddMMM`), force a password change on next login, reset the failed login
counter, and clear any account lock. Admin action — `UpdatedBy` is the calling admin's
user ID.

**Postgres:**
```sql
INSERT INTO nova_auth.tenant_user_auth
    (tenant_id, user_id, password_hash, must_change_password,
     totp_enabled, failed_login_count,
     frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
VALUES
    (@TenantId, @TargetUserId, @PasswordHash, true,
     false, 0,
     false, @UpdatedBy, @Now, @UpdatedBy, @Now, 'Nova.Presets.Api')
ON CONFLICT (tenant_id, user_id) DO UPDATE SET
    password_hash        = EXCLUDED.password_hash,
    must_change_password = true,
    failed_login_count   = 0,
    locked_until         = NULL,
    updated_on           = EXCLUDED.updated_on,
    updated_by           = EXCLUDED.updated_by,
    updated_at           = EXCLUDED.updated_at
```

**MariaDB:**
```sql
INSERT INTO `nova_auth`.`tenant_user_auth`
    (`tenant_id`, `user_id`, `password_hash`, `must_change_password`,
     `totp_enabled`, `failed_login_count`,
     `frz_ind`, `created_by`, `created_on`, `updated_by`, `updated_on`, `updated_at`)
VALUES
    (@TenantId, @TargetUserId, @PasswordHash, 1,
     0, 0,
     0, @UpdatedBy, @Now, @UpdatedBy, @Now, 'Nova.Presets.Api')
ON DUPLICATE KEY UPDATE
    `password_hash`        = VALUES(`password_hash`),
    `must_change_password` = 1,
    `failed_login_count`   = 0,
    `locked_until`         = NULL,
    `updated_on`           = VALUES(`updated_on`),
    `updated_by`           = VALUES(`updated_by`),
    `updated_at`           = VALUES(`updated_at`)
```

**MSSQL:**
```sql
MERGE INTO [yourdb].nova_auth.tenant_user_auth WITH (HOLDLOCK) AS target
USING (SELECT @TenantId AS tenant_id, @TargetUserId AS user_id) AS source
      ON target.tenant_id = source.tenant_id
     AND target.user_id   = source.user_id
WHEN MATCHED THEN
    UPDATE SET password_hash        = @PasswordHash,
               must_change_password = 1,
               failed_login_count   = 0,
               locked_until         = NULL,
               updated_on           = @Now,
               updated_by           = @UpdatedBy,
               updated_at           = 'Nova.Presets.Api'
WHEN NOT MATCHED THEN
    INSERT (tenant_id, user_id, password_hash, must_change_password,
            totp_enabled, failed_login_count,
            frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
    VALUES (@TenantId, @TargetUserId, @PasswordHash, 1,
            0, 0,
            0, @UpdatedBy, @Now, @UpdatedBy, @Now, 'Nova.Presets.Api');
```

---

### Q18 — Update avatar URL after file upload (UPDATE)

| | |
|---|---|
| **File** | `Endpoints/UploadAvatarEndpoint.cs` |
| **Method** | `HandleAsync(...)` |
| **Endpoint** | `POST /api/v1/user-profile/avatar` |
| **Database** | AuthDb |
| **Tables** | `nova_auth.tenant_user_profile` |
| **Operation** | UPDATE |

**Intent:** After the avatar file has been written to disk, store the public URL in the
profile row so subsequent profile reads return the new avatar. Returns 404 if no profile
row exists (file is still written — cleanup is not attempted).

```sql
UPDATE nova_auth.tenant_user_profile
SET    avatar_url = @AvatarUrl,
       updated_on = @Now,
       updated_by = @UserId,
       updated_at = 'Nova.Presets.Api'
WHERE  tenant_id = @TenantId
AND    user_id   = @UserId
```

---

### Q19 — Fetch users by role (SELECT with XXXX wildcard)

| | |
|---|---|
| **File** | `Endpoints/PresetsDbHelper.cs` |
| **Method** | `UsersByRoleQuerySql(AuthDbSettings, string[], string[]?)` |
| **Endpoint** | `POST /api/v1/users/by-role` |
| **Database** | AuthDb |
| **Tables** | `nova_auth.user_security_rights`, `nova_auth.tenant_user_profile` |
| **Operation** | SELECT list |

**Intent:** Return all users who hold at least one of the requested role codes within the
caller's company/branch scope, joined to their display name. The `XXXX` sentinel in
`company_code` or `branch_code` means "applies to all". Results are ordered by display
name, then role code. The endpoint groups rows by `user_id` in application code so each
user appears once with a `roles[]` array.

**With `branch_code_filter` values (specific branch list):**
```sql
SELECT  r.user_id         AS UserId,
        p.display_name    AS DisplayName,
        r.role_code       AS RoleCode
FROM    nova_auth.user_security_rights  r
JOIN    nova_auth.tenant_user_profile   p ON p.tenant_id = r.tenant_id
                                         AND p.user_id   = r.user_id
WHERE   r.tenant_id    = @TenantId
AND     (r.company_code = @CompanyCode OR r.company_code = 'XXXX')
AND     r.branch_code  IN @BranchFilter
AND     r.role_code    IN @RoleCodes
AND     r.frz_ind      = false
AND     p.frz_ind      = false
ORDER BY p.display_name, r.role_code
```

**Without branch filter (caller's own branch + XXXX wildcard):**
```sql
SELECT  r.user_id         AS UserId,
        p.display_name    AS DisplayName,
        r.role_code       AS RoleCode
FROM    nova_auth.user_security_rights  r
JOIN    nova_auth.tenant_user_profile   p ON p.tenant_id = r.tenant_id
                                         AND p.user_id   = r.user_id
WHERE   r.tenant_id    = @TenantId
AND     (r.company_code = @CompanyCode OR r.company_code = 'XXXX')
AND     (r.branch_code  = @BranchCode  OR r.branch_code  = 'XXXX')
AND     r.role_code    IN @RoleCodes
AND     r.frz_ind      = false
AND     p.frz_ind      = false
ORDER BY p.display_name, r.role_code
```

Role codes accepted: `ops_manager` → `OPSMGR`, `ops_exec` → `OPSEXEC` (mapped in
application code before the query runs). Default when `roles[]` is omitted: `OPSMGR` and
`OPSEXEC`.

---

### Q20 — Tour generics catalogue (SELECT)

| | |
|---|---|
| **File** | `Endpoints/TourGenericsEndpoint.cs` |
| **Method** | `HandleCatalogueAsync(...)` |
| **Endpoint** | `POST /api/v1/groups/tour-generics` |
| **Database** | PresetsDb |
| **Tables** | `presets.tour_generics` |
| **Operation** | SELECT list |

**Intent:** Return the full active tour generics catalogue for the tenant, ordered by name.
Typically a few hundred rows. The frontend loads this once and uses Fuse.js for
client-side fuzzy search.

```sql
SELECT code AS Code,
       name AS Name
FROM   presets.tour_generics
WHERE  tenant_id = @TenantId
AND    frz_ind   = false
ORDER  BY name
```

---

### Q21 — Tour generics typeahead search (SELECT with LIKE)

| | |
|---|---|
| **File** | `Endpoints/TourGenericsEndpoint.cs` |
| **Method** | `HandleSearchAsync(...)` |
| **Endpoint** | `POST /api/v1/groups/tour-generics/search` |
| **Database** | PresetsDb |
| **Tables** | `presets.tour_generics` |
| **Operation** | SELECT list with LIKE + paging |

**Intent:** Server-side per-keystroke typeahead fallback used when the catalogue exceeds
~2,000 entries or `tenantConfig.search.tgMode === 'like'`. Searches `code` or `name`
(controlled by request `field` param, defaults to `name`). Limit is clamped to 1–100,
default 20. Pattern is `%{query}%` (contains match).

```sql
-- {colName} is 'code' or 'name' (validated in application code, never user-supplied raw)
SELECT code AS Code,
       name AS Name
FROM   presets.tour_generics
WHERE  tenant_id  = @TenantId
AND    {colName}  LIKE @Pattern
AND    frz_ind    = false
ORDER  BY {colName}
OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY   -- MSSQL / Postgres syntax
-- MariaDB equivalent: LIMIT @Limit
```

`@Pattern` = `'%' + query + '%'`. The column name (`code` / `name`) is selected in
application code from a two-value allowlist before being interpolated — it is **not**
user-controlled.

---

### Q22 — List group task templates (SELECT)

| | |
|---|---|
| **File** | `Endpoints/TasksEndpoint.cs` |
| **Method** | `HandleListAsync(...)` |
| **Endpoint** | `POST /api/v1/tasks` |
| **Database** | PresetsDb |
| **Tables** | `presets.group_tasks` |
| **Operation** | SELECT list |

**Intent:** Return all task templates for the tenant. `sort_order` controls display order;
NULL rows sort last (`COALESCE(sort_order, 2147483647)`). `frz_ind = true` rows are
excluded unless the request includes `include_frozen: true`.

**Without frozen rows (default):**
```sql
SELECT code                        AS Code,
       name                        AS Name,
       required                    AS Required,
       critical                    AS Critical,
       group_task_sla_offset_days  AS GroupTaskSlaOffsetDays,
       reference_date              AS ReferenceDate,
       source                      AS Source,
       sort_order                  AS SortOrder,
       frz_ind                     AS FrzInd
FROM   presets.group_tasks
WHERE  tenant_id = @TenantId
AND    frz_ind   = false
ORDER  BY COALESCE(sort_order, 2147483647), code
```

**With frozen rows (`include_frozen: true`):** omit `AND frz_ind = false`.

---

### Q23 — Save (partial update) task template (UPDATE)

| | |
|---|---|
| **File** | `Endpoints/TasksEndpoint.cs` |
| **Method** | `HandleSaveAsync(...)` |
| **Endpoint** | `PATCH /api/v1/tasks/{code}` |
| **Database** | PresetsDb |
| **Tables** | `presets.group_tasks` |
| **Operation** | UPDATE (partial — dynamic SET clauses) |

**Intent:** Update only the fields supplied in the request body. Any combination of
`name`, `required`, `critical`, `group_task_sla_offset_days`, `reference_date`, `source`,
`frz_ind` may be included. Omitted fields are not touched. Setting `frz_ind: true`
soft-deletes the template. Returns 404 if no row matches the code for this tenant.

```sql
-- {setClauses} is built dynamically from only the supplied fields:
UPDATE presets.group_tasks
SET    {setClauses},
       updated_on = @Now,
       updated_by = @UpdatedBy,
       updated_at = 'Nova.Presets.Api'
WHERE  tenant_id = @TenantId
AND    code      = @Code
```

---

### Q24 — Verify codes before reorder (SELECT)

| | |
|---|---|
| **File** | `Endpoints/TasksEndpoint.cs` |
| **Method** | `HandleReorderAsync(...)` — verification step |
| **Endpoint** | `PATCH /api/v1/tasks/reorder` |
| **Database** | PresetsDb |
| **Tables** | `presets.group_tasks` |
| **Operation** | SELECT list (inside transaction) |

**Intent:** Before applying sort order changes, verify that every submitted code exists
for this tenant. If any code is unknown, the transaction is rolled back and a 409 is
returned with an `unknown_codes` extension listing the bad values.

```sql
SELECT code
FROM   presets.group_tasks
WHERE  tenant_id = @TenantId
AND    code IN @Codes
```

`@Codes` is the list of all codes from the reorder request body.

---

### Q25 — Apply sort order per code (UPDATE, inside transaction)

| | |
|---|---|
| **File** | `Endpoints/TasksEndpoint.cs` |
| **Method** | `HandleReorderAsync(...)` — update loop |
| **Endpoint** | `PATCH /api/v1/tasks/reorder` |
| **Database** | PresetsDb |
| **Tables** | `presets.group_tasks` |
| **Operation** | UPDATE (one statement per code, within a single transaction) |

**Intent:** Write the new `sort_order` for each code. Runs inside the same transaction as
Q24; all updates commit together or roll back together. Each statement is issued
individually in a loop (not a batch) so the affected-row count can be checked.

```sql
UPDATE presets.group_tasks
SET    sort_order = @SortOrder,
       updated_on = @Now,
       updated_by = @UpdatedBy,
       updated_at = 'Nova.Presets.Api'
WHERE  tenant_id  = @TenantId
AND    code       = @Code
```

---

## Summary table

| # | Endpoint | File | Method | DB | Tables | Operation |
|---|---|---|---|---|---|---|
| Q01 | POST /api/v1/branches | PresetsDbHelper.cs | `BranchesQuery` | PresetsDb | `branch`, `company` | SELECT |
| Q02 | POST /api/v1/user-profile | UserProfileEndpoint.cs | `HandleAsync` | AuthDb | `tenant_user_profile` | SELECT |
| Q03 | POST /api/v1/user-profile | UserProfileEndpoint.cs | `HandleAsync` | PresetsDb | `tenant_user_status` | SELECT |
| Q04 | POST /api/v1/user-profile | PresetsDbHelper.cs | `UserPermissionsQuerySql` | PresetsDb | `tenant_user_permissions` | SELECT |
| Q05 | POST /api/v1/user-profile/status-options | PresetsDbHelper.cs | `StatusOptionsQuerySql` | PresetsDb | `user_status_options` | SELECT (CTE) |
| Q06 | PATCH /api/v1/user-profile/status | PresetsDbHelper.cs | `FindStatusOptionSql` | PresetsDb | `user_status_options` | SELECT (CTE) |
| Q07 | PATCH /api/v1/user-profile/status | PresetsDbHelper.cs | `StatusUpsertSql` | PresetsDb | `tenant_user_status` | UPSERT |
| Q08 | PATCH /api/v1/user-profile/status | UpdateStatusEndpoint.cs | `HandleAsync` | AuthDb | `tenant_user_profile` | SELECT |
| Q09 | POST /api/v1/user-profile/change-password | ChangePasswordEndpoint.cs | `HandleAsync` | AuthDb | `tenant_user_auth` | SELECT |
| Q10 | POST /api/v1/user-profile/change-password | ChangePasswordEndpoint.cs | `HandleAsync` | AuthDb | `tenant_user_profile` | SELECT scalar |
| Q11 | POST /api/v1/user-profile/change-password | ChangePasswordEndpoint.cs | `HandleAsync` | PresetsDb | `tenant_password_change_requests` | DELETE |
| Q12 | POST /api/v1/user-profile/change-password | ChangePasswordEndpoint.cs | `HandleAsync` | PresetsDb | `tenant_password_change_requests` | INSERT |
| Q13 | POST /api/v1/user-profile/confirm-password-change | ConfirmPasswordChangeEndpoint.cs | `HandleAsync` | PresetsDb | `tenant_password_change_requests` | SELECT |
| Q14 | POST /api/v1/user-profile/confirm-password-change | ConfirmPasswordChangeEndpoint.cs | `HandleAsync` | AuthDb | `tenant_user_auth` | UPDATE |
| Q15 | POST /api/v1/user-profile/confirm-password-change | ConfirmPasswordChangeEndpoint.cs | `HandleAsync` | PresetsDb | `tenant_password_change_requests` | UPDATE |
| Q16 | POST /api/v1/user/default-password | DefaultPasswordEndpoint.cs | `HandleAsync` | AuthDb | `tenant_user_profile` | SELECT scalar |
| Q17 | POST /api/v1/user/default-password | PresetsDbHelper.cs | `DefaultPasswordUpsertSql` | AuthDb | `tenant_user_auth` | UPSERT |
| Q18 | POST /api/v1/user-profile/avatar | UploadAvatarEndpoint.cs | `HandleAsync` | AuthDb | `tenant_user_profile` | UPDATE |
| Q19 | POST /api/v1/users/by-role | PresetsDbHelper.cs | `UsersByRoleQuerySql` | AuthDb | `user_security_rights`, `tenant_user_profile` | SELECT |
| Q20 | POST /api/v1/groups/tour-generics | TourGenericsEndpoint.cs | `HandleCatalogueAsync` | PresetsDb | `tour_generics` | SELECT |
| Q21 | POST /api/v1/groups/tour-generics/search | TourGenericsEndpoint.cs | `HandleSearchAsync` | PresetsDb | `tour_generics` | SELECT (LIKE) |
| Q22 | POST /api/v1/tasks | TasksEndpoint.cs | `HandleListAsync` | PresetsDb | `group_tasks` | SELECT |
| Q23 | PATCH /api/v1/tasks/{code} | TasksEndpoint.cs | `HandleSaveAsync` | PresetsDb | `group_tasks` | UPDATE |
| Q24 | PATCH /api/v1/tasks/reorder | TasksEndpoint.cs | `HandleReorderAsync` | PresetsDb | `group_tasks` | SELECT |
| Q25 | PATCH /api/v1/tasks/reorder | TasksEndpoint.cs | `HandleReorderAsync` | PresetsDb | `group_tasks` | UPDATE |
