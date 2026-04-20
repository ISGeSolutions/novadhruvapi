INSERT INTO nova_auth.dbo.tenant_user_profile (
  tenant_id, user_id, email, display_name, avatar_url, 
  frz_ind, created_by, created_on, updated_by, updated_on, updated_at) 
VALUES (
  'BTDK', 'ISG', 'rajeev.jha.rj@gmail.com', 'Rajeev Jha', '', 
  0, 'Auto', '2026-04-13 03:45:00', 'Auto', '2026-04-13 03:45:00', 'PRESET');

--default password: changeMe@ddMMM => changeMe@13Apr

--### 1. tenant_secrets — required for /auth/token
--**Step 1 — generate the hash using Nova.Cipher:**
dotnet run --project src/tools/Nova.Cipher -- argon2 my-client-secret
argon2id:65536:3:1:3asi4ebnPJkX6WzrhuWcxQ==:yoEVrcU7hjOeytY7nyXuyrEkNtn1HwAvIWW1xiCigGQ=

--**Step 2 — verify the hash before inserting:**
dotnet run --project src/tools/Nova.Cipher -- verify "my-client-secret" 
  "argon2id:65536:3:1:3asi4ebnPJkX6WzrhuWcxQ==:yoEVrcU7hjOeytY7nyXuyrEkNtn1HwAvIWW1xiCigGQ="

--**Step 3 — insert into the database:**
INSERT INTO nova_auth.dbo.tenant_secrets
  (tenant_id, client_secret_hash, frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
VALUES
  ('BTDK', 'argon2id:65536:3:1:3asi4ebnPJkX6WzrhuWcxQ==:yoEVrcU7hjOeytY7nyXuyrEkNtn1HwAvIWW1xiCigGQ=', 
   0, 'Auto', getdate(), 'Auto', getdate(), 'PRESET');
   

--### 2. tenant_user_auth + tenant_user_profile — required for /auth/login
--User credentials also use Argon2id. Generate the password hash  
--dotnet run --project src/tools/Nova.Cipher -- argon2 "darkStar@1508"
argon2id:65536:3:1:5aLDlwLBdOy3EVk2f3jGCw==:zQL7B4h64DAbSXOty5LU3axuTD2kp7xkV6grl8vbzFA=

--To confirm the hash is correct before inserting:
--dotnet run --project src/tools/Nova.Cipher -- verify "darkStar@1508" "argon2id:65536:3:1:5aLDlwLBdOy3EVk2f3jGCw==:zQL7B4h64DAbSXOty5LU3axuTD2kp7xkV6grl8vbzFA="  

--insert/update DB
--record exists, hence update
select * from nova_auth.dbo.tenant_user_auth WHERE tenant_id = 'BTDK' AND user_id = 'ISG'

--if does not exist
INSERT INTO nova_auth.dbo.tenant_user_auth
  (tenant_id, user_id, password_hash, totp_enabled, failed_login_count,
   must_change_password, frz_ind,
   created_by, created_on, updated_by, updated_on, updated_at)
VALUES
  ('BTDK', 'ISG', 'argon2id:65536:3:1:<salt>:<hash>', 0, 0,
   0, 0,
   'SYS', GETUTCDATE(), 'SYS', GETUTCDATE(), 'Nova.Cipher');

--if exists
UPDATE nova_auth.dbo.tenant_user_auth
  SET password_hash = 'argon2id:65536:3:1:5aLDlwLBdOy3EVk2f3jGCw==:zQL7B4h64DAbSXOty5LU3axuTD2kp7xkV6grl8vbzFA=',
      failed_login_count = 0,
      locked_until = NULL
  WHERE tenant_id = 'BTDK' AND user_id = 'ISG'   

--### 3. tenant_config — required for /tenant-config
select * from nova_auth.dbo.tenant_config where tenant_id = 'BTDK'

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
 
--### 4. tenant_user_auth — enable TOTP for BTDK/ISG (required for /auth/verify-2fa)
--**Step 1 — add the secret to your TOTP app**
----Open Google Authenticator, Authy, or any RFC 6238 compatible app and add an entry manually:
----Note: JBSWY3DPEHPK3PXP is a random secret
--
--| Field           | Value                   |
--|-----------------|-------------------------|
--| Account name    | `BTDK / ISG (Nova dev)` |
--| Secret (Base32) | `JBSWY3DPEHPK3PXP`      |
--Optionals
--| Algorithm       | SHA1 (default)          |
--| Digits          | 6 (default)             |
--| Period          | 30 seconds (default)    |

----get the encrypted value:
ENCRYPTION_KEY="customersatisfactionthroughtechnicalexcellence" \
  ./src/tools/Nova.Cipher/bin/Release/net10.0/nova-cipher encrypt "JBSWY3DPEHPK3PXP"

--**Step 2 — run the seed SQL**
UPDATE nova_auth.dbo.tenant_user_auth
SET    totp_enabled          = 1,
       totp_secret_encrypted = 'kvMz9ZiRGtQNj7RVt3OUiCUAon5OkZs15NVdPf3gCpg0x5DWIYjKtnyKXfnWIAMt',
       updated_by            = 'SYS',
       updated_on            = GETUTCDATE(),
       updated_at            = 'PRESET'
WHERE  tenant_id = 'BTDK'
AND    user_id   = 'ISG';

--**Step 3 — Postman test flow**
1. **POST /api/v1/auth/login** — body `{ "tenant_id": "BTDK", "user_id": "ISG", "password": "MyPassword123!" }`.  
   Response will be `{ "requires_2fa": true, "session_token": "..." }` — no JWT is issued yet.  
   The Tests script captures `session_token` automatically to `{{session_token}}`.

--success

2. **POST /api/v1/auth/verify-2fa** — body `{ "session_token": "{{session_token}}", "code": "<6-digit code from TOTP app>" }`.  
   Response is the full login response with JWT + refresh token.

Note: the 2FA session expires after `Auth:TwoFaSessionExpiryMinutes` (default 5 min). If the session expires, repeat from step 1.

--success

**To disable TOTP again** (restore to password-only login):

UPDATE nova_auth.dbo.tenant_user_auth SET 
  totp_enabled = 0, 
  updated_by = 'ISG', updated_on = GETUTCDATE(), updated_at = 'PRESET' 
  WHERE tenant_id = 'BTDK' AND user_id = 'ISG';  
  
---forgot password testing
---done on calling the end-point 1 of 2

SELECT email FROM nova_auth.dbo.tenant_user_profile
WHERE tenant_id = 'BTDK' AND user_id = 'ISG' AND frz_ind = 0

---done on calling the end-point 2 of 2
CommandText
INSERT INTO nova_auth.dbo.tenant_auth_tokens (id, tenant_id, user_id, token_hash, token_type, expires_on, used_on, created_on)
VALUES (@Id, @TenantId, @UserId, @TokenHash, 'password_reset', @ExpiresOn, NULL, @Now)
ConnectionId
0HNKSSPU74B93
Parameters
Id=019da058-33e2-7ab3-9dc4-6c56a9e47cc4, TenantId=BTDK, UserId=ISG, TokenHash=F2A3473AA980961E0BDD5C4DCC3D3F24074560C166B84BA00DD56D2596D1FACA, ExpiresOn=18/04/2026 12:27:08 +00:00, Now=18/04/2026 11:27:08 +00:00
RequestId
0HNKSSPU74B93:00000001
RequestPath
/api/v1/auth/forgot-password
SourceContext
Nova.Shared.Data.DbConnectionFactory

