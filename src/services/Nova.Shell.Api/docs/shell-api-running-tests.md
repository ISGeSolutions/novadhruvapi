# Nova.Shell.Api — Running Tests

Guide for running the automated test suite for Nova.Shell.Api.

Test conventions and rationale are documented separately in `docs/test-conventions.md`.

---

## Prerequisites

| Requirement | Why | Notes |
|---|---|---|
| .NET 10 SDK | Build and run the test project | `dotnet --version` → `10.x` |
| Docker (running) | Testcontainers pulls a Redis image for cache/lock endpoint tests | Redis tests are skipped if Docker is not available — other tests still pass |

No `ENCRYPTION_KEY` environment variable is required. The test host replaces
`CipherService` with `PassthroughCipherService`, which returns configuration values
as-is. All secrets in the test `appsettings.json` are plaintext.

---

## Running the Tests

### All tests

From the repository root:

```bash
dotnet test src/tests/Nova.Shell.Api.Tests/
```

### With verbose output

```bash
dotnet test src/tests/Nova.Shell.Api.Tests/ --logger "console;verbosity=detailed"
```

### Specific test class

```bash
dotnet test src/tests/Nova.Shell.Api.Tests/ --filter "FullyQualifiedName~HelloWorldEndpointTests"
```

### Specific test method

```bash
dotnet test src/tests/Nova.Shell.Api.Tests/ \
  --filter "FullyQualifiedName~Given_AnonymousRequest_When_HelloWorldIsCalled_Then_Returns200"
```

### Skip Redis container tests (no Docker)

```bash
dotnet test src/tests/Nova.Shell.Api.Tests/ --filter "FullyQualifiedName!~RedisHealth"
```

### Build without running

```bash
dotnet build src/tests/Nova.Shell.Api.Tests/Nova.Shell.Api.Tests.csproj
```

Expected: `0 Warning(s). 0 Error(s).`

---

## Test Configuration Files

The test project has its own configuration files at:

```
src/tests/Nova.Shell.Api.Tests/
  appsettings.json       — plaintext secrets; minimum tenant definition
  opsettings.json        — OutboxRelay and RateLimiting disabled for tests
  migrationpolicy.json   — identical to the service version
```

All three files are copied to the test output directory on build. The test host
runs with `ASPNETCORE_ENVIRONMENT=Test`.

---

## Minimum Required Config

These entries have no safe default and **must remain consistent** between
`appsettings.json` and the test constants in `Helpers/TestConstants.cs`.
They ship with matching values — but if you change one, you must change the other.

### `appsettings.json` — `Jwt` section

```json
"Jwt": {
  "Issuer":    "https://auth.nova.internal",
  "Audience":  "nova-api",
  "SecretKey": "nova-test-signing-key-minimum-32-chars-x"
}
```

**Why this matters:** The test host validates incoming JWTs using `Jwt.SecretKey`
(via `AddNovaJwt`). `JwtFactory.CreateToken` signs tokens using `TestConstants.JwtSecret`.
If these two values diverge, all authenticated endpoint tests return `401 Unauthorized`
with no other indication of the cause.

**Minimum length:** The secret must be at least 32 characters. Shorter values cause
`Microsoft.IdentityModel.Tokens` to throw at startup.

**Rule:** If `TestConstants.JwtSecret` is changed, update `appsettings.json` `Jwt.SecretKey`
to match. If `appsettings.json` `Jwt.Issuer` or `Jwt.Audience` are changed, update
`TestConstants.JwtIssuer` / `TestConstants.JwtAudience` to match.

---

### `appsettings.json` — `Tenants` array

```json
"Tenants": [
  {
    "TenantId":         "BLDK",
    "DisplayName":      "Blixen Tours (Test)",
    "DbType":           "MsSql",
    "ConnectionString": "unused-in-phase1-tests",
    "SchemaVersion":    "legacy",
    "BrokerType":       "Redis",
    "BorkerTypesAllowed": [ "Redis" ]
  }
]
```

**Why this matters:** `TenantRegistry` is built at startup from this array. If it is
empty, the registry throws and the test host fails to start. At least one tenant entry
is required even if no DB calls are made.

**Why `ConnectionString` is a placeholder:** Phase 1 tests (hello-world, health) do not
open any database connections. The value is stored but never used. Phase 2 tests
(authenticated endpoints) will require a real or test-container connection string here.

**Rule:** `TestConstants.TenantId` must match the `TenantId` of an entry in this array.
If you add or rename a tenant, update `TestConstants.TenantId` accordingly.

---

## Optional Config

These entries have working defaults. Change them only if the situation requires it.

### `appsettings.json` — `ConnectionStrings.redis`

```json
"ConnectionStrings": {
  "redis": "localhost:6379"
}
```

**Default:** `localhost:6379`.

This value is overridden at runtime by `TestHost.CreateWithRedis(connectionString)` for
any test that uses `RedisFixture`. It only applies to tests that call `TestHost.Create()`
(the stateless overload) and happen to exercise a Redis path — which no Phase 1 test does.

Change this if you have a persistent local Redis on a non-standard port and want the
stateless test host to connect to it instead of using a Testcontainers container.

---

### `opsettings.json` — `Logging.DefaultLevel`

```json
"Logging": {
  "DefaultLevel": "Warning"
}
```

**Default:** `Warning` — keeps test output clean.

Set to `Debug` or `Information` temporarily when diagnosing a failing test. Revert
afterwards to avoid flooding the test runner output.

---

### `opsettings.json` — `Caching.Profiles`

```json
"Caching": {
  "Profiles": {
    "ReferenceData": { "Layer": "Redis", "TtlSeconds": 60, "Enabled": true }
  }
}
```

**Default:** 60 second TTL.

Reduce `TtlSeconds` (e.g. to `5`) if writing cache expiry tests that need to wait
for a key to expire. Do not set to `0` — the cache layer treats that as "no expiry".

---

### `opsettings.json` — `RateLimiting.Enabled`

```json
"RateLimiting": {
  "Enabled": false
}
```

**Default:** `false` (disabled in tests).

Leave disabled. Enabling rate limiting in tests causes flaky failures when multiple
tests run in rapid succession against the same in-process host and share the rate
limiter's partition state.

---

### `appsettings.json` — `OpenTelemetry.OtlpEndpoint`

```json
"OpenTelemetry": {
  "OtlpEndpoint": "http://localhost:4317"
}
```

**Default:** `http://localhost:4317`.

The test host initialises OpenTelemetry but OTLP export failures are non-fatal —
tests pass regardless of whether a collector is running. No change required.

---

## Log and Result Files

Every `dotnet test` run produces three output files. All land in `TestResults/` inside the test
project directory (`src/tests/Nova.Shell.Api.Tests/TestResults/`).

```
TestResults/
  shell-api-tests.trx              ← test results (TRX/XML)
  Logs/
    shell-api-test-{yyyyMMdd}.json ← structured JSON application log
    shell-api-test-{yyyyMMdd}.log  ← plain-text application log
```

`TestResults/` is git-ignored — do not commit it.

---

### `shell-api-tests.trx` — Test results

Standard .NET TRX format (XML). Contains every test's name, outcome (Passed/Failed/Skipped),
duration, failure message, and full stack trace. **Start here when a CI run fails.**

AI review workflow:
1. Parse the TRX for `<UnitTestResult outcome="Failed">` entries.
2. Read `<Output><ErrorInfo><Message>` and `<StackTrace>` to identify the failure.
3. Cross-reference with the JSON log for the request and response that triggered it.

---

### `shell-api-test-{date}.json` — Structured application log

One JSON object per line (newline-delimited). Written by the test host's Serilog logger
at **Information** level and above. Microsoft/System framework logs are suppressed at Warning
to reduce noise.

Each entry contains:

| Field | Content |
|---|---|
| `Timestamp` | ISO 8601 UTC — `"2026-04-05T10:23:01.123Z"` |
| `Level` | `"Information"`, `"Warning"`, `"Error"`, `"Fatal"` |
| `MessageTemplate` | Serilog template with `{Property}` placeholders |
| `RenderedMessage` | Fully rendered message with values substituted |
| `Properties` | All structured fields: `RequestId`, `RequestPath`, `StatusCode`, `CorrelationId`, `TenantId`, etc. |
| `Exception` | Full exception message + stack trace (present only on Error/Fatal) |

Example entry for a failed request:

```json
{
  "Timestamp": "2026-04-05T10:23:01.123Z",
  "Level": "Error",
  "MessageTemplate": "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms",
  "RenderedMessage": "HTTP GET /api/v1/hello-world responded 500 in 12.3 ms",
  "Properties": {
    "RequestMethod": "GET",
    "RequestPath": "/api/v1/hello-world",
    "StatusCode": 500,
    "Elapsed": 12.3,
    "CorrelationId": "abc-123"
  },
  "Exception": "System.InvalidOperationException: ...\n   at ..."
}
```

AI review workflow:
1. Filter entries where `Level` is `"Error"` or `"Fatal"`.
2. Read `RenderedMessage` + `Exception` for the root cause.
3. Read `Properties.CorrelationId` to trace the full request lifecycle across entries.

---

### `shell-api-test-{date}.log` — Plain-text application log

Same events as the JSON file, formatted for human reading during local debugging:

```
2026-04-05T10:23:01.123Z [ERR] Nova.Shell.Api.Endpoints.HelloWorldEndpoint
  Hello-world handler threw unexpectedly {"CorrelationId": "abc-123"}
  System.InvalidOperationException: ...
     at ...
```

Format: `{Timestamp} [{Level}] {SourceContext} {RenderedMessage} {Properties}{NewLine}{Exception}`

---

### Parallelisation and log ordering

Tests run **sequentially** (configured in `xunit.runner.json` and `.runsettings`). This means
log entries appear in the same order as test execution, with no interleaving between test
classes. Sequential ordering makes log review and root-cause tracing significantly easier.

---

## What Phase 1 Tests Cover

| Test class | Endpoint | Auth | External deps |
|---|---|---|---|
| `HelloWorldEndpointTests` | `GET /api/v1/hello-world` | Anonymous | None |
| `HealthEndpointTests` | `GET /health` | Anonymous | None (RabbitMQ expected absent) |
| `HealthEndpointTests.RedisHealthTests` | `GET /health/redis` | Anonymous | Docker (Testcontainers Redis) |
| `HealthEndpointTests` | `GET /health/rabbitmq` | Anonymous | None (confirms endpoint is wired; 503 expected) |

**Not covered in Phase 1:** authenticated endpoints (`/echo`, `/echo-list`), cache
endpoints (`/test-cache`), lock endpoint (`/test-lock`), DB endpoints, internal auth.
These are addressed in Phase 2 and later phases.

---

## Interpreting Failures

| Symptom | Likely cause |
|---|---|
| All tests fail at startup with `InvalidOperationException` | `ENCRYPTION_KEY` env var is set and non-empty — the real `CipherService` is being loaded instead of `PassthroughCipherService`. Unset the variable for the test run. |
| All authenticated tests return `401` | `Jwt.SecretKey` in `appsettings.json` does not match `TestConstants.JwtSecret`. Align the two values. |
| `tenant_id` mismatch returns `403` | `TestConstants.TenantId` does not match any `TenantId` in the `Tenants` array. |
| Redis tests fail with `DockerNotAvailableException` | Docker is not running. Start Docker, or exclude Redis tests with `--filter "FullyQualifiedName!~RedisHealth"`. |
| `opsettings.json not found` | The file was not copied to the output directory. Verify `CopyToOutputDirectory: Always` is set in the `.csproj` for all three config files. |
