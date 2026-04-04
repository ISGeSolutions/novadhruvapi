# Nova.Shell.Api — Postman Testing Guide

Step-by-step verification of every endpoint and platform behaviour using the Postman collection.
Follow each section in order. Each test states exactly what to send and what to check.

---

## Prerequisites

### 1. Start the API

```bash
export ENCRYPTION_KEY=your-dev-key
dotnet run --project src/services/Nova.Shell.Api
```

Confirm it is listening:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5100
```

Alternatively use `aspire run` from the repo root (starts all services + dashboard).

### 2. Import all three files into Postman

| File | Purpose |
|---|---|
| `planning/postman/Nova.Auth.postman_collection.json` | Generates JWT tokens for authenticated requests |
| `planning/postman/Nova.Shell.Api.postman_collection.json` | All Shell API endpoints and scenarios |
| `planning/postman/Nova.Local.Dev.postman_environment.json` | Pre-configured environment with default values |

**How to import:** Postman → Import → select the file. Import all three.

### 3. Set up the `Nova — Local Dev` environment

After importing `Nova.Local.Dev.postman_environment.json`, open the environment and set:

| Variable | Action |
|---|---|
| `jwt_secret` | Enter your plaintext JWT secret — decrypt `appsettings.json` → `Jwt.SecretKey` using your `ENCRYPTION_KEY`. Never commit this value. |
| `tenant_id` / `company_id` / `branch_id` / `user_id` | Pre-filled with BLDK defaults — change if your test tenant differs. |

Select **Nova — Local Dev** from the environment dropdown (top-right) before running any requests.

---

## Part 1 — Generate a Dev JWT

**Collection:** Nova — Auth (Dev Token Generator)  
**Request:** `1. Generate Dev JWT → Generate Dev JWT + Verify`

**Send the request.**

### What to check

| Check | Expected |
|---|---|
| Pre-request script ran without error | Console (bottom panel) shows `access_token generated.` with `tenant_id` and `user_id` |
| `access_token` variable set | Postman environment → `access_token` is now populated (a long string in three dot-separated segments) |
| Response status | `200 OK` from `GET /api/v1/hello-world` |
| Response body | `{ "message": "Hello, World!", "timestamp": "...", "correlation_id": "...", "dep_date": "2026-08-15", "created_on": "..." }` |
| `api-supported-versions` header | `1.0` (confirms API versioning is active) |

If you see **401** — the `jwt_secret` environment variable does not match the decrypted `Jwt.SecretKey` in `appsettings.json`. Check your `ENCRYPTION_KEY` and re-decrypt.

---

## Part 2 — Diagnostics

**Collection:** Nova.Shell.Api → **Diagnostics**

### Test 2.1 — Hello World (smoke test)

**Request:** `GET /api/v1/hello-world`

**Send the request.**

| Check | Expected |
|---|---|
| Status | `200 OK` |
| `Content-Type` header | `application/json` |
| `X-Correlation-ID` header | Present — a UUID string |
| `api-supported-versions` header | `1.0` |
| `message` field | `"Hello, World!"` |
| `timestamp` field | ISO 8601 UTC — e.g. `"2026-04-03T10:00:00+00:00"` |
| `correlation_id` field | **snake_case** — same value as the `X-Correlation-ID` response header |
| `dep_date` field | `"2026-08-15"` — **no time, no offset** (`DateOnly` → `yyyy-MM-dd`) |
| `created_on` field | ISO 8601 UTC — e.g. `"2026-04-03T10:00:00+00:00"` (`DateTimeOffset` → `yyyy-MM-ddThh:mm:ssZ`) |

**Date handling verification:**
- `dep_date` must be exactly `"2026-08-15"` with no time component and no `T` or `Z` suffix. If you see a time component, `DateOnly` is not being used.
- `created_on` must end with `+00:00` or `Z`. If you see a local offset (e.g. `+05:30`), `DateTimeOffset.UtcNow` is not being used.

**Verify snake_case:** the field must be `correlation_id`, not `correlationId`. If you see camelCase, the `AddNovaJsonOptions()` registration is missing or incorrect.

---

### Test 2.2 — HTTP Ping (resilient HttpClient reference)

**Request:** `GET /api/v1/http-ping`

**Send the request.** (The API must be running — the endpoint calls back to its own `/health`.)

| Check | Expected |
|---|---|
| Status | `200 OK` |
| `target` field | `"http://localhost:5100/health"` |
| `is_success` field | `true` |
| `status` field | `200` |
| `latency_ms` field | A positive integer (round-trip latency in ms) |
| `X-Correlation-ID` response header | Present — same ID was forwarded to the outbound call |

This confirms `AddNovaHttpClient()` is registered, the resilience pipeline is active, and correlation IDs propagate to downstream services.

---

### Test 2.3 — Unsupported version → 400

**Request:** `GET /api/v99/hello-world`

| Check | Expected |
|---|---|
| Status | `400 Bad Request` |
| `Content-Type` | `application/problem+json` |
| `extensions.correlation_id` | Present |

This confirms `AddNovaApiVersioning()` is active. An unknown version is rejected before hitting the handler.

---

### Test 2.4 — Correlation ID round-trip

**Request:** `GET /api/v1/hello-world`  
**Add header:** `X-Correlation-ID: test-my-trace-001`

| Check | Expected |
|---|---|
| `X-Correlation-ID` response header | `test-my-trace-001` (echoed back) |
| `correlation_id` in response body | `test-my-trace-001` (same value) |

This confirms `CorrelationIdMiddleware` reads the incoming header rather than generating a new one.

---

### Test 2.5 — Test DB MSSQL

**Request:** `GET /test-db/mssql`

| Check (DB available) | Expected |
|---|---|
| Status | `200 OK` |
| Body | JSON array — each object has `code`, `value`, `dep_date`, `updated_on` |
| `dep_date` format | `"yyyy-MM-dd"` — no time, no offset (e.g. `"2019-03-15"`) |
| `updated_on` format | `"yyyy-MM-ddThh:mm:ss+00:00"` — ISO 8601, always `+00:00` (e.g. `"2024-11-01T10:00:00+00:00"`) |

| Check (DB unavailable) | Expected |
|---|---|
| Status | `503 Service Unavailable` |
| Body | `{ "error": "...", "db": "mssql" }` |

---

### Test 2.6 — Test DB Postgres

**Request:** `GET /test-db/postgres`

| Check (DB available) | Expected |
|---|---|
| Status | `200 OK` |
| Body | JSON array of `{ "code": "...", "value": "..." }` objects |

| Check (DB unavailable) | Expected |
|---|---|
| Status | `503 Service Unavailable` |
| Body | `{ "error": "...", "db": "postgres" }` |

---

## Part 3 — Problem Details (RFC 9457)

All tests in this section verify `UseNovaProblemDetails` and `AddNovaProblemDetails` are working.

**Common checks that apply to every error response in this section:**

| Check | Expected |
|---|---|
| `Content-Type` header | `application/problem+json` (not `application/json`) |
| `extensions.correlation_id` | Present — matches the `X-Correlation-ID` response header |
| `extensions.trace_id` | Present — a non-empty string |
| `instance` field | **Absent** (suppressed — must not expose server paths) |
| Stack trace in body | **Absent** — the response body must contain no exception details |

---

### Test 3.1 — Unknown Route → 404 Problem Details

**Collection:** Echo (Reference) → `Echo — 404 Unknown Route (Problem Details)`  
**Request:** `GET /does-not-exist`

| Check | Expected |
|---|---|
| Status | `404 Not Found` |
| `Content-Type` | `application/problem+json` |
| `status` field | `404` |
| `title` field | `"Not Found"` |
| `extensions.correlation_id` | Present |
| `extensions.trace_id` | Present |

This confirms `UseStatusCodePages()` is active — ASP.NET Core converts the 404 into Problem Details format.

---

## Part 4 — Echo Endpoint (Validation Convention)

**Collection:** Nova.Shell.Api → **Echo (Reference — Validation + Problem Details)**

> **Prerequisite:** `POST /api/v1/echo` requires a JWT. Complete Part 1 (Generate Dev JWT) first. The Shell API collection attaches `Authorization: Bearer {{access_token}}` automatically — ensure the `Nova — Local Dev` environment is selected and `access_token` is populated.

This endpoint is the reference implementation of the Nova validation convention. Each request below tests one step of the mandatory validation order.

---

### Test 4.1 — Happy Path

**Request:** `Echo — Happy Path`

Body sent:
```json
{
  "tenant_id": "{{tenant_id}}",
  "company_id": "{{company_id}}",
  "branch_id": "{{branch_id}}",
  "user_id": "{{user_id}}",
  "browser_locale": "en-GB",
  "browser_timezone": "Europe/London",
  "ip_address": "203.0.113.42",
  "message": "Hello from Postman"
}
```

| Check | Expected |
|---|---|
| Status | `200 OK` |
| `Content-Type` | `application/json` |
| `echo` field | `"Hello from Postman"` |
| `received_at` field | ISO 8601 UTC datetime |
| `correlation_id` field | snake_case, matches `X-Correlation-ID` response header |
| All response keys | snake_case (`received_at`, `correlation_id` — not camelCase) |

---

### Test 4.2 — Step 1: Missing Context Fields → 400

**Request:** `Echo — 400 Missing Context Fields`

Body sent (no context fields, only message):
```json
{
  "message": "Hello"
}
```

| Check | Expected |
|---|---|
| Status | `400 Bad Request` |
| `Content-Type` | `application/problem+json` |
| `title` | `"Validation failed"` |
| `errors.tenant_id` | `["tenant_id is required."]` |
| `errors.company_id` | `["company_id is required."]` |
| `errors.branch_id` | `["branch_id is required."]` |
| `errors.user_id` | `["user_id is required."]` |
| `extensions.correlation_id` | Present |

This confirms `RequestContextValidator.Validate()` runs first, before any domain logic.

---

### Test 4.3 — Step 3: Missing Domain Field → 400

**Request:** `Echo — 400 Missing Domain Field`

Body sent (all context fields present, message is empty string):
```json
{
  "tenant_id": "{{tenant_id}}",
  "company_id": "{{company_id}}",
  "branch_id": "{{branch_id}}",
  "user_id": "{{user_id}}",
  "browser_locale": "en-GB",
  "browser_timezone": "Europe/London",
  "message": ""
}
```

| Check | Expected |
|---|---|
| Status | `400 Bad Request` |
| `Content-Type` | `application/problem+json` |
| `title` | `"Validation failed"` |
| `errors.message` | `["message is required."]` |
| `errors` does NOT contain | `tenant_id`, `company_id`, `branch_id`, `user_id` (context fields passed) |

This confirms domain validation runs only after context fields are confirmed present.

---

### Test 4.4 — Business Rule Violation → 404

**Request:** `Echo — 404 Not Found`

Body sent (`message` set to the trigger value `"not-found"`):
```json
{
  "tenant_id": "{{tenant_id}}",
  "company_id": "{{company_id}}",
  "branch_id": "{{branch_id}}",
  "user_id": "{{user_id}}",
  "browser_locale": "en-GB",
  "browser_timezone": "Europe/London",
  "message": "not-found"
}
```

| Check | Expected |
|---|---|
| Status | `404 Not Found` |
| `Content-Type` | `application/problem+json` |
| `title` | `"Resource not found"` |
| `detail` | `"No resource matches the supplied identifier."` |
| `extensions.correlation_id` | Present |

---

### Test 4.5 — Unhandled Exception → 500 (no stack trace)

**Request:** `Echo — 500 Unhandled Exception`

Body sent (`message` set to `"throw"`):
```json
{
  "tenant_id": "{{tenant_id}}",
  "company_id": "{{company_id}}",
  "branch_id": "{{branch_id}}",
  "user_id": "{{user_id}}",
  "browser_locale": "en-GB",
  "browser_timezone": "Europe/London",
  "message": "throw"
}
```

| Check | Expected |
|---|---|
| Status | `500 Internal Server Error` |
| `Content-Type` | `application/problem+json` |
| `title` | `"An error occurred while processing your request."` |
| `extensions.correlation_id` | Present |
| `extensions.trace_id` | Present |
| Body contains exception message | **NO** — `"Deliberate exception..."` must NOT appear in the body |
| Body contains stack trace | **NO** — no `at ...` lines anywhere in the body |

This is a critical security check. If you see the exception message or a stack trace, `UseNovaProblemDetails()` is not wired correctly.

---

## Part 5 — Pagination Contract

**Collection:** Nova.Shell.Api → **Echo List (Reference — Pagination Contract)**

> **Prerequisite:** `POST /api/v1/echo/list` requires a JWT. Complete Part 1 first and ensure `access_token` is set in your environment.

`POST /api/v1/echo/list` demonstrates the `PagedRequest` / `PagedResult<T>` contract. It simulates a 47-item dataset so all pagination boundary conditions can be tested without a database.

---

### Test 5.1 — Happy Path (page 1)

**Request:** `Echo List — Happy Path (page 1)`

Body sent:
```json
{
  "tenant_id": "{{tenant_id}}",
  "company_id": "{{company_id}}",
  "branch_id": "{{branch_id}}",
  "user_id": "{{user_id}}",
  "browser_locale": "en-GB",
  "browser_timezone": "Europe/London",
  "page_number": 1,
  "page_size": 10
}
```

| Check | Expected |
|---|---|
| Status | `200 OK` |
| `items` | Array of 10 objects, each with `id` and `label` |
| `total_count` | `47` |
| `page_number` | `1` |
| `page_size` | `10` |
| `total_pages` | `5` |
| `has_next_page` | `true` |
| `has_previous_page` | `false` |
| All response keys | snake_case (`total_count`, `page_number`, `page_size`, `total_pages`, `has_next_page`, `has_previous_page`) |

---

### Test 5.2 — Last Page (page 5)

**Request:** `Echo List — Last Page (page 5)`

Change `page_number` to `5`, `page_size` to `10`. Items 41–47 = 7 items on the final page.

| Check | Expected |
|---|---|
| Status | `200 OK` |
| `items` count | `7` (items 41–47 — last partial page) |
| `total_count` | `47` |
| `total_pages` | `5` |
| `has_next_page` | `false` |
| `has_previous_page` | `true` |

---

### Test 5.3 — Invalid page_number → 400

**Request:** `Echo List — 400 Invalid page_number`

Send `page_number: 0` (below minimum of 1).

| Check | Expected |
|---|---|
| Status | `400 Bad Request` |
| `Content-Type` | `application/problem+json` |
| `title` | `"Validation failed"` |
| `errors.page_number` | `["page_number must be 1 or greater."]` |
| `errors` does NOT contain | `tenant_id`, `company_id` etc. (context fields passed — validator ran in order) |

---

### Test 5.4 — page_size over maximum → 400

**Request:** `Echo List — 400 Invalid page_size`

Send `page_size: 250` (exceeds max of 100).

| Check | Expected |
|---|---|
| Status | `400 Bad Request` |
| `Content-Type` | `application/problem+json` |
| `errors.page_size` | `["page_size must be between 1 and 100."]` |

---

### Test 5.5 — Default values (omit page_number and page_size)

Send the full context fields but omit `page_number` and `page_size` entirely.

| Check | Expected |
|---|---|
| Status | `200 OK` |
| `page_number` in response | `1` (default) |
| `page_size` in response | `25` (default) |
| `items` count | `25` (first 25 of 47) |
| `total_pages` | `2` (⌈47/25⌉) |

This confirms defaults are applied when fields are absent from the request body.

---

## Part 6 — Health Checks

**Collection:** Nova.Shell.Api → **Health Checks**

---

### Test 6.1 — Aggregate Health

**Request:** `Health — Aggregate` → `GET /health`

| Check (all DBs available) | Expected |
|---|---|
| Status | `200 OK` |
| `status` | `"Healthy"` |
| `results.mssql.status` | `"Healthy"` |
| `results.postgres.status` | `"Healthy"` |
| `results.mariadb.status` | `"Healthy"` |

| Check (any DB down) | Expected |
|---|---|
| Status | `503 Service Unavailable` |
| `status` | `"Unhealthy"` |
| Failing DB entry | `"status": "Unhealthy"` with `description` and `exception` fields |

---

### Test 6.2 — MSSQL Health

**Request:** `Health — MSSQL` → `GET /health/mssql`

| Check | Expected |
|---|---|
| Status | `200 OK` or `503` |
| Body | Contains only `mssql` entry (no postgres, no mariadb) |

---

### Test 6.3 — Postgres Health

**Request:** `Health — Postgres` → `GET /health/postgres`

| Check | Expected |
|---|---|
| Status | `200 OK` or `503` |
| Body | Contains only `postgres` entry |

---

### Test 6.4 — MariaDB Health

**Request:** `Health — MariaDB` → `GET /health/mariadb`

| Check | Expected |
|---|---|
| Status | `200 OK` or `503` |
| Body | Contains only `mariadb` entry |

---

### Test 6.5 — Redis Health

**Request:** `GET /health/redis`

| Check | Expected |
|---|---|
| Status | `200 OK` |
| Body | `{ "status": "Healthy" }` |

If this returns `503`, Redis is not reachable. Check that the Docker container is running (`docker ps`) or that `aspire run` started successfully.

---

## Part 7 — Rate Limiting

Per-tenant rate limiting applies to all `/api/v1/...` endpoints. Health checks and `/test-db/...` are unversioned and not rate-limited.

> Rate limiting is controlled by `opsettings.json → RateLimiting`. Changes take effect immediately without a restart.

### Test 7.1 — Health checks are not rate-limited

`GET /health` and `GET /test-db/mssql` must never return 429 regardless of call frequency.

| Check | Expected |
|---|---|
| Status | `200` (or `503` if DB is unavailable) — never `429` |

### Test 7.2 — 429 response format

To trigger a 429 without hammering the API, temporarily set `PermitLimit: 1` in `opsettings.json`, then send two requests to `GET /api/v1/hello-world` in quick succession.

| Check | Expected |
|---|---|
| Status on second request | `429 Too Many Requests` |
| `Content-Type` | `application/problem+json` |
| `type` | `https://tools.ietf.org/html/rfc6585#section-4` |
| `title` | `Too Many Requests` |
| `status` | `429` |
| `correlation_id` | Present — matches `X-Correlation-ID` response header |
| `trace_id` | Present |
| `Retry-After` response header | Present (seconds until window resets) |

Restore `PermitLimit: 100` in `opsettings.json` after this test.

### Test 7.3 — Rate limiting disabled (emergency bypass)

Set `Enabled: false` in `opsettings.json`. Send requests beyond the configured `PermitLimit`.

| Check | Expected |
|---|---|
| Status | Never `429` — all requests succeed |

Restore `Enabled: true` after this test.

---

## Part 8 — Cross-Cutting Checks

These checks apply across all endpoints and confirm platform-level behaviour.

### 8.1 — snake_case enforced on all responses

Send any request and inspect the response body.

| Check | Expected |
|---|---|
| All JSON keys | `snake_case` — e.g. `correlation_id`, `received_at`, `tenant_id` |
| No camelCase keys | `correlationId`, `receivedAt` must NOT appear |

If camelCase appears, `AddNovaJsonOptions()` → `ConfigureHttpJsonOptions` with `JsonNamingPolicy.SnakeCaseLower` is not registered or not applied.

---

### 8.2 — Correlation ID on every response

Send any request **without** an `X-Correlation-ID` request header.

| Check | Expected |
|---|---|
| `X-Correlation-ID` response header | Present — a generated UUID |

Now send the same request **with** `X-Correlation-ID: my-custom-id-999`.

| Check | Expected |
|---|---|
| `X-Correlation-ID` response header | `my-custom-id-999` (echoed back, not overwritten) |

---

### 8.3 — Problem Details on every error

Send `GET /does-not-exist`.

| Check | Expected |
|---|---|
| `Content-Type` | `application/problem+json` |
| `status` field in body | Matches the HTTP status code |
| `extensions.correlation_id` | Matches `X-Correlation-ID` response header |
| `extensions.trace_id` | Present |
| `instance` field | **Absent** |

---

### 8.4 — UTC timestamps

On any response containing a datetime field (`timestamp`, `received_at`, `created_on`, `updated_on`):

| Check | Expected |
|---|---|
| Format | ISO 8601 with `Z` suffix — e.g. `"2026-04-03T10:00:00Z"` |
| Calendar date fields | `"yyyy-MM-dd"` — e.g. `"2026-08-15"` — no `T`, no offset |
| Timezone offset | Always `Z` — never `+00:00`, never a local offset like `+05:30` |

---

## Part 9 — Redis Cache

The `/test-cache` diagnostic endpoint demonstrates `ICacheService.GetOrSetAsync` (cache-aside) and `InvalidateAsync`. Uses the `ReferenceData` profile (`opsettings.json`) — Redis layer, 1 hr TTL.

### Test 9.1 — Cache miss → hit cycle

**Step 1 — First call (cache miss)**

**Request:** `GET /test-cache`

| Check | Expected |
|---|---|
| Status | `200 OK` |
| Body | `{ "cached_at": "yyyy-MM-ddTHH:mm:ssZ" }` |
| Source | Factory ran — note the `cached_at` value |

**Step 2 — Repeat call (cache hit)**

**Request:** `GET /test-cache` (same request, no delay needed)

| Check | Expected |
|---|---|
| Status | `200 OK` |
| `cached_at` | **Identical** to Step 1 — Redis returned the cached value, factory did not run |

---

### Test 9.2 — Invalidation resets the cache

**Step 3 — Invalidate**

**Request:** `DELETE /test-cache`

| Check | Expected |
|---|---|
| Status | `200 OK` |
| Body | `{ "invalidated": "nova:test:hello" }` |

**Step 4 — Call after invalidation (cache miss again)**

**Request:** `GET /test-cache`

| Check | Expected |
|---|---|
| Status | `200 OK` |
| `cached_at` | **Different** from Step 1 — factory ran again after the key was removed |

---

### Test 9.3 — Kill switches (hot-reload, no restart)

Edit `opsettings.json` while the API is running. Changes take effect immediately.

| Scenario | Setting | Expected behaviour |
|---|---|---|
| Disable all caching | `Caching.GloballyEnabled: false` | `GET /test-cache` always returns a new `cached_at` — factory always called |
| Emergency disable | `Caching.EmergencyDisable: true` | Same as above — overrides everything |
| Disable one profile | `Caching.Profiles.ReferenceData.Enabled: false` | Factory always called for this profile |
| Dry run | `Caching.DryRunMode: true` | Cache is read and written but result never served; factory always called |

Restore all settings to defaults after testing.

---

## Part 10 — Distributed Locking

The `/test-lock` endpoint demonstrates `IDistributedLockService`. It uses the key `nova:test:lock`.

- Normal call (`GET /test-lock`) — acquires the lock, does no real work, releases immediately.
- Hold call (`GET /test-lock?hold=N`) — acquires and holds the lock for N seconds (max 30). Use this to test contention from a second client.

---

### Test 10.1 — Normal acquire and release

**Request:** `GET /test-lock`

| Check | Expected |
|---|---|
| Status | `200 OK` |
| `acquired` | `true` |
| `resource` | `"nova:test:lock"` |
| `held_for_ms` | A small value (< 100 ms) — lock was acquired and released immediately |

Repeat the request — it should return `200` every time. Each call acquires and releases the lock within the same request.

---

### Test 10.2 — Contention (409 response)

This test requires two HTTP clients open simultaneously (two Postman tabs, or Postman + curl).

**Step 1 — Hold the lock from Client A**

**Request:** `GET /test-lock?hold=10`

Do not wait for it to complete. Keep this request in-flight.

**Step 2 — Try to acquire from Client B (immediately)**

**Request:** `GET /test-lock`

| Check | Expected |
|---|---|
| Status | `409 Conflict` |
| `acquired` | `false` |
| `reason` | `"Lock is held by another instance, or Redis is unavailable."` |

**Step 3 — Wait for Client A to finish (after 10 s)**

Client A returns `200` with `held_for_seconds: 10`.

**Step 4 — Retry from Client B**

**Request:** `GET /test-lock`

| Check | Expected |
|---|---|
| Status | `200 OK` — lock is available again |

---

### Test 10.3 — TTL expiry (automatic release)

This confirms that Redis releases the lock automatically if the holder does not dispose it (e.g. process crash scenario).

1. Use `redis-cli` or RedisInsight to set the key manually:
   ```
   SET nova:test:lock manual-token PX 20000
   ```
2. `GET /test-lock` immediately → `409 Conflict` (lock held)
3. Wait 20 seconds
4. `GET /test-lock` → `200 OK` — TTL expired, lock gone

---

## Summary Checklist

Use this as a sign-off checklist before marking a test session complete.

| # | Area | Check | Pass/Fail |
|---|---|---|---|
| 1 | JWT | `access_token` generated and stored in environment | |
| 2 | Versioning | `GET /api/v1/hello-world` returns `200` with `api-supported-versions: 1.0` header | |
| 3 | Versioning | `GET /api/v99/hello-world` returns `400 application/problem+json` (unsupported version) | |
| 4 | Versioning | `GET /hello-world` (no version prefix) returns `404` | |
| 5 | Smoke | `GET /api/v1/hello-world` returns `200` with `message`, `timestamp`, `correlation_id` | |
| 6 | HttpClient | `GET /api/v1/http-ping` returns `200` with `target`, `status=200`, `is_success=true`, `latency_ms` | |
| 7 | HttpClient | `X-Correlation-ID` header present on `/http-ping` response (forwarded to outbound call) | |
| 8 | snake_case | `correlation_id` (not `correlationId`) in response | |
| 9 | Correlation ID | Custom `X-Correlation-ID` header is echoed back in response | |
| 10 | Validation | `POST /api/v1/echo` with empty body returns `400` with `errors` for all 4 required fields | |
| 11 | Validation | `POST /api/v1/echo` with valid context but empty message returns `400` with `errors.message` | |
| 12 | Problem Details | `POST /api/v1/echo` `message=not-found` returns `404 application/problem+json` | |
| 13 | Problem Details | `POST /api/v1/echo` `message=throw` returns `500` — no stack trace in body | |
| 14 | Problem Details | `GET /does-not-exist` returns `404 application/problem+json` | |
| 15 | Problem Details | Every error response has `extensions.correlation_id` and `extensions.trace_id` | |
| 16 | Problem Details | No response has `instance` field | |
| 17 | Pagination | `POST /api/v1/echo/list` page 1 returns 10 items, `total_count=47`, `has_next_page=true` | |
| 18 | Pagination | `POST /api/v1/echo/list` page 5 returns 7 items, `has_next_page=false`, `has_previous_page=true` | |
| 19 | Pagination | `page_number=0` returns `400` with `errors.page_number` | |
| 20 | Pagination | `page_size=250` returns `400` with `errors.page_size` | |
| 21 | Pagination | Omitting `page_number`/`page_size` uses defaults (1 and 25) | |
| 22 | Rate Limiting | `429` returned on breach with `application/problem+json` body and `Retry-After` header | |
| 23 | Rate Limiting | `correlation_id` present in 429 response body | |
| 24 | Rate Limiting | `GET /health` never returns `429` (unversioned — not rate-limited) | |
| 25 | Rate Limiting | Setting `Enabled: false` in `opsettings.json` bypasses limiting immediately | |
| 26 | Health | `GET /health` returns `200` with `mssql`, `postgres`, `mariadb` entries | |
| 27 | Health | Individual health endpoints return only their own DB entry | |
| 28 | Health | `GET /health/redis` returns `200 { "status": "Healthy" }` | |
| 29 | DB | `GET /test-db/mssql` returns `200` array or `503 { error, db }` | |
| 30 | DB | `GET /test-db/postgres` returns `200` array or `503 { error, db }` | |
| 31 | Cache | `GET /test-cache` twice returns same `cached_at` on second call (cache hit) | |
| 32 | Cache | `DELETE /test-cache` then `GET /test-cache` returns new `cached_at` (invalidation works) | |
| 33 | Cache | `Caching.GloballyEnabled: false` in `opsettings.json` causes factory to always run (no restart) | |
| 34 | Lock | `GET /test-lock` returns `200 { acquired: true }` | |
| 35 | Lock | `GET /test-lock?hold=10` then immediately `GET /test-lock` returns `409 { acquired: false }` | |
| 36 | Lock | After hold expires, `GET /test-lock` returns `200` again (lock released) | |

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| API won't start | `ENCRYPTION_KEY` not set | `export ENCRYPTION_KEY=your-dev-key` |
| `GET /hello-world` times out | API not running or wrong port | Check it's on `http://localhost:5100` |
| `Missing variable: jwt_secret` in pre-request script | No environment selected, or `jwt_secret` not set | Select **Nova — Local Dev** from the environment dropdown and ensure `jwt_secret` is filled in |
| `401` from JWT verify step | `jwt_secret` wrong | Re-decrypt `Jwt.SecretKey` from `appsettings.json` using correct `ENCRYPTION_KEY` |
| `correlationId` (camelCase) in response | snake_case not applied | Check `builder.Services.AddNovaJsonOptions()` is in `Program.cs` |
| Error response is plain JSON not `application/problem+json` | Problem Details not wired | Check `builder.Services.AddNovaProblemDetails()` and `app.UseNovaProblemDetails()` are in `Program.cs` |
| Stack trace appears in 500 response | `UseNovaProblemDetails` not first in pipeline | `app.UseNovaProblemDetails()` must be the first middleware call |
| `500` on `/echo` happy path | `TenantContext` is null (not resolved) | Check `TenantResolutionMiddleware` is registered and the tenant exists in `appsettings.json` |
| `errors.tenant_id` missing from 400 | `RequestContextValidator.Validate()` not called | Check step 1 of `EchoEndpoint.Handle` is in place |
