# Running and Testing the API

## Prerequisites

### Encryption key

`ENCRYPTION_KEY` must be set in your shell before starting the API. The app throws `InvalidOperationException` at boot if it is missing.

```bash
export ENCRYPTION_KEY=your-dev-key
```

The key must match the key used to encrypt the connection strings and JWT secret in `appsettings.json`.

### Redis

Required for caching, distributed locking, and outbox relay (Redis-broker tenants).

When running via **Aspire**, Redis is pulled and started automatically as a Docker container — nothing to do.

When running **standalone**, start Redis manually:

```bash
docker run -d --name nova-redis -p 6379:6379 redis:alpine
```

Connection string in `appsettings.json → ConnectionStrings:redis` defaults to `localhost:6379`.

### RabbitMQ

Required for the outbox relay when any tenant has `"BrokerType": "RabbitMq"` (the default).

When running via **Aspire**, you need to start RabbitMQ manually (it is not yet wired into the AppHost):

```bash
docker run -d --name nova-rabbitmq \
  -p 5672:5672 \
  -p 15672:15672 \
  rabbitmq:management
```

- Port `5672` — AMQP (used by the relay)
- Port `15672` — Management UI: `http://localhost:15672` (login: `guest` / `guest`)

When running **standalone**, the same command applies.

The RabbitMQ password in `appsettings.json → RabbitMq.Password` must be encrypted with your `ENCRYPTION_KEY`. For local dev with the default `guest` password, encrypt it and replace the placeholder:

```bash
# Encrypt using ICipherService — or use the test endpoint /test-internal-auth/token
# pattern to confirm the service starts cleanly with your encrypted password.
```

To disable the outbox relay entirely during dev (if you have no broker running), set in `opsettings.json`:
```json
{ "OutboxRelay": { "Enabled": false } }
```
Takes effect immediately — no restart needed.

---

## Running via Aspire AppHost (preferred for multi-service dev)

Starts all registered services plus the Aspire dashboard in one command.

```bash
export ENCRYPTION_KEY=your-dev-key
aspire run
```

Run from the repo root. The `aspire.config.json` at the root tells the CLI where the AppHost is.

The dashboard login URL (e.g. `https://localhost:17100/login?t=<token>`) is printed to the console on startup.
Nova.Shell.Api launches automatically — its assigned port is shown in the dashboard resource list.

`ENCRYPTION_KEY` is inherited from the parent shell and forwarded automatically to all child services.

**One-time setup** — install the Aspire CLI global tool (not a workload):
```bash
dotnet tool install -g aspire.cli
# Add to PATH if not already present:
export PATH="$PATH:/Users/rajeevjha/.dotnet/tools"
# Add the export line to ~/.zshrc to make it permanent
```

---

## Running Nova.Shell.Api standalone

Use this when you only need the Shell API without the Aspire dashboard.

### Standard web mode

```bash
cd src/services/Nova.Shell.Api
dotnet run
```

Listens on `http://localhost:5100`.

### Specify a launch profile explicitly

```bash
dotnet run --launch-profile http      # HTTP only — http://localhost:5100
dotnet run --launch-profile https     # HTTPS + HTTP — https://localhost:5101 + http://localhost:5100
dotnet run --launch-profile console   # Console mode (see below)
```

Profiles are defined in `src/services/Nova.Shell.Api/Properties/launchSettings.json`.

### Console mode

Runs the full web host but prints verbose startup output to stdout: config loaded, DB pings, tenant count, OTel endpoint. Useful for diagnosing startup issues.

```bash
dotnet run --launch-profile console
# or equivalently from any directory:
dotnet run --project src/services/Nova.Shell.Api -- --console
# or via environment variable:
RUN_AS_CONSOLE=true dotnet run --project src/services/Nova.Shell.Api
```

### Watch mode

Auto-restarts the API on file save. Use during active development.

```bash
cd src/services/Nova.Shell.Api
dotnet watch run
```

### From the repo root

```bash
dotnet run --project src/services/Nova.Shell.Api
dotnet run --project src/services/Nova.Shell.Api --launch-profile console
```

---

## Running from VS Code

Four debug configurations are provided in `.vscode/launch.json`:

| Configuration | Description |
|---|---|
| **Nova AppHost (Aspire)** | Starts all services + Aspire dashboard. Use this for normal dev. |
| **Nova.Shell.Api (HTTP)** | Runs Shell API standalone on http://localhost:5100. |
| **Nova.Shell.Api (HTTPS)** | Runs Shell API with HTTPS on https://localhost:5101. |
| **Nova.Shell.Api (Console)** | Passes `--console` — verbose startup pings appear in Debug Console. |

`ENCRYPTION_KEY` is inherited from your shell environment. Set it before launching VS Code, or add it to your shell profile (`~/.zshrc` / `~/.bashrc`).

### Setting breakpoints

Set breakpoints in any `.cs` file and press **F5** to hit them. All configurations attach the debugger automatically.

---

## Testing from Postman

### One-time setup

1. Import all three files from `postman/`:
   - `Nova.Auth.postman_collection.json` — JWT token generator
   - `Nova.Shell.Api.postman_collection.json` — all Shell API endpoints
   - `Nova.Local.Dev.postman_environment.json` — pre-configured environment
2. Open the **Nova — Local Dev** environment and set `jwt_secret` to the plaintext JWT secret (decrypt `appsettings.json → Jwt.SecretKey` using your `ENCRYPTION_KEY`).
3. Select **Nova — Local Dev** from the environment dropdown.
4. Run **Generate Dev JWT + Verify** in the Auth collection — this stores `access_token` automatically.

For the full step-by-step test plan see `postman/Nova.Shell.Api.testing-guide.md`.

### Endpoints

All responses use snake_case JSON (e.g. `correlation_id`, not `correlationId`).
Business endpoints are versioned under `/api/v1/`. Health and diagnostic endpoints are unversioned.

| Request | Auth | Body | Expected (happy path) |
|---|---|---|---|
| `GET {{baseUrl}}/api/v1/hello-world` | None | — | 200 `{ message, timestamp, correlation_id, dep_date, created_on }` + `api-supported-versions: 1.0` header |
| `GET {{baseUrl}}/api/v1/http-ping` | None | — | 200 `{ target, status, is_success, latency_ms }` |
| `POST {{baseUrl}}/api/v1/echo` | JWT | `{"message":"hello", ...context}` | 200 `{ echo, received_at, correlation_id }` |
| `POST {{baseUrl}}/api/v1/echo/list` | JWT | `{"page_number":1,"page_size":10, ...context}` | 200 `{ items, total_count, page_number, total_pages, ... }` |
| `GET {{baseUrl}}/test-db/mssql` | None | — | 200 JSON array of `{ code, value, dep_date, updated_on }` — `dep_date` is `"yyyy-MM-dd"`, `updated_on` is `"yyyy-MM-ddThh:mm:ssZ"` |
| `GET {{baseUrl}}/test-db/postgres` | None | — | 200 JSON array of `{ code, value }` |
| `GET {{baseUrl}}/test-cache` | None | — | 200 `{ cached_at }` — same value on repeated calls until invalidated or TTL expires |
| `DELETE {{baseUrl}}/test-cache` | None | — | 200 `{ invalidated: "nova:test:hello" }` |
| `GET {{baseUrl}}/test-lock` | None | — | 200 `{ acquired: true, resource, held_for_ms }` or 409 if already locked |
| `GET {{baseUrl}}/test-lock?hold=10` | None | — | 200 after 10 s — holds the lock; call `GET /test-lock` from a second client to trigger 409 |
| `GET {{baseUrl}}/test-internal-auth/token` | None | — | 200 `{ token: "..." }` — current outbound service JWT |
| `GET {{baseUrl}}/test-internal-auth/protected` | InternalJwt | — | 200 `{ caller, issued_at }` — or 401 without a valid internal token |
| `GET {{baseUrl}}/test-internal-auth/call-self` | None | — | 200 `{ round_trip: true, caller: "nova-shell" }` — full generate → call → verify round-trip |
| `GET {{baseUrl}}/health` | None | — | 200 all healthy |
| `GET {{baseUrl}}/health/mssql` | None | — | 200 `{ status: "Healthy" }` |
| `GET {{baseUrl}}/health/postgres` | None | — | 200 `{ status: "Healthy" }` |
| `GET {{baseUrl}}/health/mariadb` | None | — | 200 `{ status: "Healthy" }` |
| `GET {{baseUrl}}/health/redis` | None | — | 200 `{ status: "Healthy" }` |

### Rate Limiting

All `/api/v1/...` endpoints enforce a per-tenant fixed-window limit.
Health checks and `/test-db/...` are unversioned and **not** rate-limited.

| Scenario | How to trigger | Expected |
|---|---|---|
| Within limit | Normal requests | Responses include `X-RateLimit-*` headers (if added by future middleware) |
| Disable for debugging | Set `RateLimiting.Enabled: false` in `opsettings.json` | Rate limiting bypassed immediately — no restart needed |
| Simulate breach | Send > `PermitLimit` requests within `WindowSeconds` | `429 Too Many Requests`, `application/problem+json`, `Retry-After` header |

429 response body:
```json
{
  "type": "https://tools.ietf.org/html/rfc6585#section-4",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Request rate limit exceeded. Please reduce your request rate.",
  "correlation_id": "...",
  "trace_id": "..."
}
```

### Testing Problem Details (RFC 9457)

Verify the global exception handler is working:

| Scenario | How to trigger | Expected |
|---|---|---|
| Missing route | `GET {{baseUrl}}/does-not-exist` | 404 `application/problem+json` with `correlation_id`, `trace_id` |
| Unsupported version | `GET {{baseUrl}}/api/v99/hello-world` | 400 `application/problem+json` |
| Validation failure | `POST {{baseUrl}}/api/v1/echo` with `{"message":""}` | 400 Problem Details with `errors.message` field |
| Not found | `POST {{baseUrl}}/api/v1/echo` with full context + `{"message":"not-found"}` | 404 Problem Details |
| Unhandled exception | `POST {{baseUrl}}/api/v1/echo` with full context + `{"message":"throw"}` | 500 Problem Details — no stack trace in body |

Check every Problem Details response:
- `Content-Type: application/problem+json`
- `extensions.correlation_id` matches `X-Correlation-ID` response header
- `extensions.trace_id` is present
- No `instance` field (suppressed to avoid leaking server paths)
- No stack trace in the body

### Correlation ID

Every response includes `X-Correlation-ID` in the response headers. To trace a specific request, send your own value:

```
X-Correlation-ID: my-test-id-123
```

The same value echoes back in the response header and in the `/hello-world` response body.

### Testing Distributed Locking

The `/test-lock` endpoint demonstrates `IDistributedLockService.TryAcquireAsync` and the contention (409) path.

**Verify normal acquire/release (single client):**

| Step | Request | Expected |
|---|---|---|
| 1 | `GET {{baseUrl}}/test-lock` | 200 `{ "acquired": true, "resource": "nova:test:lock", "held_for_ms": ... }` |
| 2 | `GET {{baseUrl}}/test-lock` (repeat) | 200 again — lock was released after step 1, new acquisition |

**Verify contention (two clients):**

| Step | Client | Request | Expected |
|---|---|---|---|
| 1 | Client A | `GET {{baseUrl}}/test-lock?hold=10` | 200 — lock acquired, held for 10 s |
| 2 | Client B (immediately) | `GET {{baseUrl}}/test-lock` | 409 `{ "acquired": false, "reason": "Lock is held..." }` |
| 3 | Client A (after 10 s) | Request completes | 200 — lock released |
| 4 | Client B (retry) | `GET {{baseUrl}}/test-lock` | 200 — lock available again |

---

### Testing Redis Cache

The `/test-cache` endpoint demonstrates the `ICacheService.GetOrSetAsync` and `InvalidateAsync` pattern using the `ReferenceData` profile (Redis, 1 hr TTL).

**Verify cache hit/miss cycle (4 steps):**

| Step | Request | Expected |
|---|---|---|
| 1 | `GET {{baseUrl}}/test-cache` | 200 `{ "cached_at": "...Z" }` — factory runs, value stored in Redis |
| 2 | `GET {{baseUrl}}/test-cache` (repeat) | 200 same `cached_at` value — cache hit, factory did **not** run |
| 3 | `DELETE {{baseUrl}}/test-cache` | 200 `{ "invalidated": "nova:test:hello" }` — key removed |
| 4 | `GET {{baseUrl}}/test-cache` (repeat) | 200 new `cached_at` value — cache miss after invalidation, factory ran again |

**Verify health:**

```
GET {{baseUrl}}/health/redis  →  200 { "status": "Healthy" }
```

**Verify kill switches (hot-reload — no restart needed):**

| Scenario | Change in `opsettings.json` | Expected |
|---|---|---|
| Disable all caching | `Caching.GloballyEnabled: false` | Factory always called; Redis not touched |
| Emergency disable | `Caching.EmergencyDisable: true` | Same as above — immediate override |
| Disable one profile | `Caching.Profiles.ReferenceData.Enabled: false` | Factory always called for that profile |
| Dry run | `Caching.DryRunMode: true` | Cache is read and written but result is never served; factory always called |

---

### Testing mock error responses

To force an error response from the Postman mock server (not the real API), add:

| Header | Value |
|---|---|
| `x-mock-response-name` | e.g. `503 Service Unavailable — Connection failed` |
| `x-mock-response-code` | e.g. `503` |

See `postman/MockServer-Setup.md` for the full list of available mock responses.

---

### Testing Service-to-Service Authentication

Three diagnostic endpoints exercise the internal JWT flow end-to-end.

**Inspect the current token:**

```
GET {{baseUrl}}/test-internal-auth/token
```
Returns the current outbound `Bearer` token. Paste it at [jwt.io](https://jwt.io) to inspect claims. Expect:
- `iss` = value from `InternalAuth.ServiceName` (e.g. `nova-shell`)
- `aud` = `nova-internal`
- `exp` ≈ 5 minutes from now (300 s default lifetime)

**Verify the protected endpoint rejects unauthenticated calls:**

```
GET {{baseUrl}}/test-internal-auth/protected           # no Authorization header → 401
GET {{baseUrl}}/test-internal-auth/protected           # with a user JWT (nova-api) → 403
GET {{baseUrl}}/test-internal-auth/protected           # with the internal JWT → 200
```

**Full round-trip (generate + call + verify in one request):**

```
GET {{baseUrl}}/test-internal-auth/call-self
```
Expected: `200 { "round_trip": true, "caller": "nova-shell" }`

This proves token generation, HTTP client injection, and protected endpoint validation all work together.

**Token caching:** tokens are cached and reused until 30 s before expiry. Call `/test-internal-auth/token` repeatedly — the value does not change until near-expiry.

---

### Testing the Outbox Relay

The relay runs as a background service — there is no dedicated test endpoint. Verify it by writing a row to `nova_outbox` and watching the relay process it.

**Prerequisites:**
- RabbitMQ running (`docker run ... rabbitmq:management` — see Prerequisites above)
- `opsettings.json → OutboxRelay.Enabled: true` (default)

**Step 1 — Insert a test message directly into the DB:**

```sql
-- MSSQL (adjust for Postgres / MariaDB syntax)
INSERT INTO nova_outbox
    (aggregate_id, event_type, payload, exchange, routing_key, content_type, max_retries, status)
VALUES
    ('test-001', 'test.event', '{"hello":"world"}', 'nova.test', 'test.event',
     'application/json', 3, 'pending');
```

**Step 2 — Watch the logs** (within `PollingIntervalSeconds`, default 5 s):

```
dbug: OutboxRelayWorker — 1 pending message(s) for tenant BTDK
dbug: OutboxRelayWorker — sent <id> [nova.test/test.event] for tenant BTDK
```

**Step 3 — Confirm the row is marked sent:**

```sql
SELECT id, status, processed_at, retry_count FROM nova_outbox WHERE aggregate_id = 'test-001';
-- status = 'sent', processed_at is set
```

**Step 4 — Verify delivery in RabbitMQ Management UI:**

Open `http://localhost:15672` (login: `guest` / `guest`). Navigate to **Queues** — if a queue is bound to the `nova.test` exchange with routing key `test.event`, the message appears there. If no queue is bound, it is dropped (normal — no consumer yet).

**Testing the kill switch:**

```json
// opsettings.json
{ "OutboxRelay": { "Enabled": false } }
```

Save the file. The relay stops processing on the next cycle — no restart needed. Re-enable the same way.

**Testing retry exhaustion:**

Insert a message with `max_retries = 1` and configure an invalid exchange name that RabbitMQ rejects. After one failure the status becomes `failed` and `last_error` contains the exception message.

---

## Quick Reference

```bash
# Start infrastructure (Redis + RabbitMQ) — do this once; containers persist across restarts
docker run -d --name nova-redis    -p 6379:6379           redis:alpine
docker run -d --name nova-rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:management

# Run via Aspire AppHost (all services + dashboard; Redis/RabbitMQ must already be running)
export ENCRYPTION_KEY=your-dev-key
aspire run          # run from repo root — Docker must be running

# Run Shell API standalone (requires Redis + RabbitMQ on localhost)
export ENCRYPTION_KEY=your-dev-key
dotnet run --project src/services/Nova.Shell.Api

# Run Shell API standalone (console mode — verbose startup)
dotnet run --project src/services/Nova.Shell.Api -- --console

# Watch (auto-restart on save)
cd src/services/Nova.Shell.Api && dotnet watch run

# Build only
dotnet build src/services/Nova.Shell.Api

# Restore packages
dotnet restore
```
