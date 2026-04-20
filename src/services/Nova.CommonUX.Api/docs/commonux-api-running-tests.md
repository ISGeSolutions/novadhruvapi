# Nova.CommonUX.Api — Running Tests

> **Status: tests not yet written.**  
> This document covers the infrastructure and conventions to follow when the test suite is authored.  
> See `docs/test-conventions.md` for the authoritative cross-service conventions.

---

## Quick start (once tests exist)

```bash
cd src/tests/Nova.CommonUX.Api.Tests
dotnet test
```

---

## Test project location

```
src/tests/Nova.CommonUX.Api.Tests/
```

---

## What needs to be tested

| Test class (planned) | Endpoint | Key cases |
|---|---|---|
| `HelloWorldEndpointTests` | `POST /api/v1/hello` | 200, message body, content-type, 405 wrong method |
| `TokenEndpointTests` | `POST /api/v1/auth/token` | 422 missing tenant/secret, 401 wrong secret, 200 returns token |
| `LoginEndpointTests` | `POST /api/v1/auth/login` | 422 missing fields, 401 wrong password, 401 locked account, 200 no-2FA, 200 requires-2FA |
| `Verify2FaEndpointTests` | `POST /api/v1/auth/verify-2fa` | 422 missing session/code, 401 expired session |
| `ForgotPasswordEndpointTests` | `POST /api/v1/auth/forgot-password` | 422 missing fields, 200 always (no user enumeration) |
| `ResetPasswordEndpointTests` | `POST /api/v1/auth/reset-password` | 422 missing fields, 401 bad token |
| `MagicLinkEndpointTests` | `POST /api/v1/auth/magic-link` | 422 missing fields, 200 always |
| `TenantConfigEndpointTests` | `POST /api/v1/tenant-config` | 401 no token, 400 missing context, 403 tenant mismatch |
| `MainAppMenusEndpointTests` | `POST /api/v1/novadhruv-mainapp-menus` | 401 no token, 400 missing context, 403 tenant mismatch |
| `HealthEndpointTests` | `GET /health`, `GET /health/redis` | valid codes, JSON body, status field |

Happy-path tests for endpoints that require database access (login 200, tenant-config 200, etc.) are not included in the initial suite — they require a live database and will be added when a test database is provisioned.

---

## Infrastructure notes

Follow the same patterns established in `Nova.Presets.Api.Tests`:

- **GlobalUsings.cs** — must include `global using Xunit;`. xunit types are not auto-imported.
- **PassthroughCipherService** — inject via `ConfigureAppConfiguration` / `AddInMemoryCollection` to remove the `ENCRYPTION_KEY` dependency and allow plaintext config values in tests.
- **NoOpEmailSender** — override `IEmailSender` registration in `TestHost` to discard email. Otherwise the `forgot-password` and `magic-link` endpoints will fail at startup if no SendGrid key is configured.
- **JWT config override** — `WebApplicationFactory` loads the service's `appsettings.json` (not the test project's), which contains an encrypted `Jwt:SecretKey`. Override via `ConfigureAppConfiguration` + `AddInMemoryCollection` with plaintext `Jwt:SecretKey`, `Jwt:Issuer`, and `Jwt:Audience`.
- **Session store** — `InMemorySessionStore` is used by default when `Cache:CacheProvider` is not set to `Redis`. Tests do not need Redis.
- **Timestamp bounds** — truncate `before` bounds to the second: `DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds())`.

### TestHost pattern

```csharp
// Override cipher, email sender, and JWT config
factory.WithWebHostBuilder(builder =>
{
    builder.ConfigureAppConfiguration((_, config) =>
    {
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"]  = "test-secret-32-chars-minimum!!!!!",
            ["Jwt:Issuer"]     = "https://auth.nova.internal",
            ["Jwt:Audience"]   = "nova-api",
            ["AuthDb:ConnectionString"] = "Host=localhost;Database=test;...",
            ["AuthDb:DbType"]  = "Postgres"
        });
    });
    builder.ConfigureServices(services =>
    {
        services.AddSingleton<IEmailSender, NoOpEmailSender>();
    });
});
```

---

## Anonymous vs authenticated endpoints

The pre-request JWT generation script documents which paths are anonymous. In tests, anonymous endpoints must not have `RequireAuthorization()` and must accept requests with no `Authorization` header.

Authenticated endpoints must return `401` when called without a valid Bearer token.

---

## Output files (once tests exist)

```
src/tests/Nova.CommonUX.Api.Tests/TestResults/
  commonux-api-tests.trx                  — TRX test result file
  Logs/
    commonux-api-test-{date}.json         — structured JSON application log
    commonux-api-test-{date}.log          — plain-text application log
```
