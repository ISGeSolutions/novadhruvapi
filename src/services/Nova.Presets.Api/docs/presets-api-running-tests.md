# Nova.Presets.Api — Running Tests

## Quick start

```bash
cd src/tests/Nova.Presets.Api.Tests
dotnet test
```

**Current result: 39/39 passing.**

---

## Test project location

```
src/tests/Nova.Presets.Api.Tests/
```

---

## What is tested

| Test class | Endpoint | Tests |
|---|---|---|
| `HelloWorldEndpointTests` | `POST /api/v1/hello` | 4 — 200, message body, content-type, 405 wrong method |
| `StatusOptionsEndpointTests` | `POST /api/v1/user-profile/status-options` | 7 — 200, 5 options, field shapes, available option, 401, 400×2 |
| `UserProfileEndpointTests` | `POST /api/v1/user-profile` | 3 — 401, 400 (missing tenant/user), 403 mismatch |
| `UpdateStatusEndpointTests` | `PATCH /api/v1/user-profile/status` | 5 — 401, 400×4 (missing fields, invalid status, note too long), 403 |
| `ChangePasswordEndpointTests` | `POST /api/v1/user-profile/change-password` | 6 — 401, 400×4 (missing fields, policy fails), 403 |
| `ConfirmPasswordChangeEndpointTests` | `POST /api/v1/user-profile/confirm-password-change` | 2 — 400 missing token, endpoint reachable |
| `BranchesEndpointTests` | `POST /api/v1/branches` | 4 — 401, 400×2, 403 mismatch |
| `HealthEndpointTests` | `GET /health`, `GET /health/db` | 5 — valid codes, JSON body, status field, db health graceful |

Happy-path tests for endpoints that require database access (UserProfile 200, Branches 200, etc.) are
not included — they require a live database and will be added when a test database is provisioned.

---

## Infrastructure notes

- **No external containers required.** No Redis, no database, no RabbitMQ needed to run these tests.
- **PassthroughCipherService** — removes the `ENCRYPTION_KEY` dependency. All secrets in the test config are plaintext.
- **NoOpEmailSender** — discards all outbound email. No SendGrid API key required.
- **JWT config override** — `TestHost.Create()` injects `Jwt:SecretKey`, `Jwt:Issuer`, and `Jwt:Audience` via `ConfigureAppConfiguration`. This is required because `WebApplicationFactory` loads the service's `appsettings.json` (not the test project's), which contains an encrypted signing key.

---

## Known bugs fixed during test authoring

### Bug 1 — Stray `C:\nova\avatars` directory corrupted MSBuild glob expansion

`Program.cs` calls `Directory.CreateDirectory(avatarStorage.LocalDirectory)` at startup. On macOS, the Windows-style path `C:\nova\avatars` creates a literal directory named `C:\nova\avatars` (backslashes as filename characters) inside the service source directory. MSBuild's `**/*.resx` recursive glob then traverses this directory, hits a path normalisation exception, and fails to compile the service with `MSB3552`.

**Fix:** Delete the stray directory (`rm -rf "src/services/Nova.Presets.Api/C:\nova\avatars"`). The test config sets `AvatarStorage.LocalDirectory` to `""` so the directory is not recreated during test runs.

**Prevention:** Do not run the service directly with the production `appsettings.json` on macOS, or change the `appsettings.json` `LocalDirectory` value to a macOS-compatible path before doing so.

### Bug 2 — `/health` returned `text/plain`, not `application/json`

`Program.cs` called `app.MapHealthChecks("/health")` without configuring a JSON `ResponseWriter`. The default ASP.NET Core health check response is plain text, which broke JSON deserialization in tests and in any consumer that expects structured health output.

**Fix:** Added a `WriteJsonHealthResponse` static function and passed it as `ResponseWriter` to `MapHealthChecks`, matching the pattern used in `Nova.Shell.Api`.

---

## Output files

```
src/tests/Nova.Presets.Api.Tests/TestResults/
  presets-api-tests.trx          — TRX test result file
  Logs/
    presets-api-test-{date}.json — structured JSON application log
    presets-api-test-{date}.log  — plain-text application log
```
