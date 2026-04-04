# Nova.ToDo.Api — Running and Testing

Developer guide for running Nova.ToDo.Api locally, verifying infrastructure, running DB migrations, and testing all 21 endpoints.

---

## Prerequisites

| Dependency | Purpose | Default |
|---|---|---|
| .NET 10 SDK | Build and run | — |
| MSSQL (or Postgres / MariaDB) | Tenant database | `localhost` |
| Redis | Caching + distributed locks | `localhost:6379` |
| RabbitMQ | Outbox relay message broker | `localhost:5672` |

---

## Configuration Files

There are two config files. Both live in the project root (`src/services/Nova.ToDo.Api/`).

### `appsettings.json` — application config (restart required)

Key sections to fill in before first run:

```json
{
  "Tenants": [
    {
      "TenantId":         "BLDK",
      "DisplayName":      "Blixen Tours",
      "DbType":           "MsSql",
      "ConnectionString": "<encrypted-mssql-connection-string>",
      "SchemaVersion":    "legacy",
      "BrokerType":       "RabbitMq"
    }
  ],
  "RabbitMq": {
    "Host":        "localhost",
    "Port":        5672,
    "Username":    "guest",
    "Password":    "<encrypted-rabbitmq-password>",
    "VirtualHost": "/"
  },
  "Jwt": {
    "Issuer":    "https://auth.nova.internal",
    "Audience":  "nova-api",
    "SecretKey": "<encrypted-jwt-secret-key>"
  }
}
```

`DbType` values: `MsSql`, `Postgres`, `MariaDb`.

Connection strings and secrets are encrypted via `ICipherService`. In local dev you can use the plain-text values directly — the cipher service falls back to identity (no-op) when the dev key is in use.

### `opsettings.json` — ops config (hot-reloadable, no restart needed)

```json
{
  "ConcurrencyCheck": {
    "StrictMode": true,
    "ConflictMessage": "Record was updated between your read and update. Refresh data."
  },
  "ToDoSummary": {
    "ClientCompletedWindowDays":   15,
    "SupplierCompletedWindowDays": 30
  },
  "RateLimiting": {
    "Enabled":       true,
    "PermitLimit":   100,
    "WindowSeconds": 60
  }
}
```

Changes to `opsettings.json` are picked up live — no restart needed.

**`ConcurrencyCheck.StrictMode`** — set to `false` during initial testing to skip the `409 Conflict` check on Update / Complete / Freeze / UndoComplete. Re-enable before going to production.

---

## Running the Service

### Standard run

```bash
cd src/services/Nova.ToDo.Api
dotnet run
```

Service listens on **http://localhost:5101**.

### Console mode (connectivity diagnostics on startup)

```bash
dotnet run -- --console
# or
RUN_AS_CONSOLE=true dotnet run
```

In console mode the startup prints:

```
[Nova] Config files loaded and validated.
[Logging] DB sink disabled — enable in opsettings
[OTel] Configured. Endpoint: http://localhost:4317
[Tenancy] Tenant registry loaded. Tenants: 1
[MSSQL]    Ping skipped (Enabled: false in appsettings).
[Postgres] Ping skipped (Enabled: false in appsettings).
[MariaDB]  Ping skipped (Enabled: false in appsettings).
```

To enable a DB ping, set `DiagnosticConnections.<Engine>.Enabled: true` and supply a `ConnectionString` in `appsettings.json`.

### Build only

```bash
dotnet build src/services/Nova.ToDo.Api/Nova.ToDo.Api.csproj
```

Expected: `0 Warning(s). 0 Error(s).`

---

## Health Checks

| Endpoint | Description |
|---|---|
| `GET /health` | All registered health checks (Redis, RabbitMQ) |
| `GET /health/redis` | Redis connectivity only |
| `GET /health/rabbitmq` | RabbitMQ connectivity only |
| `GET /health/db/{tenantId}` | Per-tenant DB connectivity — runs `SELECT 1` against the tenant's connection string |

Example:

```bash
curl http://localhost:5101/health
curl http://localhost:5101/health/db/BLDK
```

A healthy DB response:

```json
{
  "tenant_id":  "BLDK",
  "db_type":    "MsSql",
  "status":     "healthy",
  "latency_ms": 4
}
```

---

## Database Migrations

Migrations are **not** run automatically at startup. Run them explicitly after each deployment.

### Run migrations for all tenants

```bash
curl -X POST http://localhost:5101/admin/migrations/run
```

### Run migrations for a single tenant

```bash
curl -X POST "http://localhost:5101/admin/migrations/run?tenantId=BLDK"
```

Example response:

```json
{
  "migrations": [
    {
      "tenant_id":       "BLDK",
      "applied":         ["V001__CreateToDo.sql"],
      "blocked":         [],
      "blocked_scripts": []
    }
  ]
}
```

SQL scripts live in `Migrations/MsSql/`, `Migrations/Postgres/`, and `Migrations/MariaDb/`. The runner selects the correct folder based on each tenant's `DbType`. Scripts prefixed `V{NNN}__` are applied in version order. Destructive scripts (DROP, TRUNCATE) are blocked automatically.

---

## Liveness Check

```bash
curl http://localhost:5101/api/v1/hello
```

Returns `200 OK` with a plain message. No auth required. Use this to confirm the service is up and routing correctly before running authenticated tests.

---

## Authentication

All 21 business endpoints require a **JWT Bearer token** (`Authorization: Bearer <token>`).

The token must contain:
- `iss` matching `Jwt.Issuer` in `appsettings.json`
- `aud` = `nova-api`
- `tenant_id` claim matching the `TenantId` of a registered tenant

For local testing, generate a token using the configured `Jwt.SecretKey` and claims above, or use the Postman collection (see below) which includes a pre-request script for token generation.

Every request body must also include the `RequestContext` fields that the frontend `apiClient` injects automatically:

```json
{
  "tenant_id":        "BLDK",
  "company_id":       "BLX",
  "branch_id":        "HQ",
  "user_id":          "JD",
  "browser_locale":   "en-GB",
  "browser_timezone": "Europe/London",
  "ip_address":       "127.0.0.1"
}
```

The `tenant_id` in the request body must match the `tenant_id` claim in the JWT. A mismatch returns `403 Forbidden`.

---

## Testing with Postman

The Postman collection is at:

```
planning/postman/Nova.ToDo.Api.postman_collection.json
```

Import into Postman (File → Import). The collection contains all 21 endpoints with example request bodies and 118 response examples covering success and error cases.

**Collection variables to set before running:**

| Variable | Value |
|---|---|
| `base_url` | `http://localhost:5101` |
| `tenant_id` | `BLDK` (or your test tenant) |
| `jwt_token` | A valid signed JWT for the tenant |

### Recommended test order

Run in this sequence to build up state correctly:

1. `GET /api/v1/hello` — confirm service is live
2. `POST /admin/migrations/run` — apply DB schema
3. `GET /health/db/BLDK` — confirm DB connectivity
4. `POST /api/v1/todos` — **Create** — note the returned `seq_no`
5. `POST /api/v1/todos/{seq_no}` — **GetBySeqNo** — verify full record
6. `POST /api/v1/todos/{seq_no}/update` — **Update** — pass `updated_on` from step 5
7. `POST /api/v1/todos/list/by-assignee` — **ListByAssignee** — verify pagination
8. `POST /api/v1/todos/summary/by-user` — **SummaryByUser** — verify counts
9. `POST /api/v1/todos/{seq_no}/complete` — **Complete** — pass `done_by` + `updated_on`
10. `POST /api/v1/todos/{seq_no}/undo-complete` — **UndoComplete**
11. `POST /api/v1/todos/{seq_no}/freeze` — **Freeze** (body: `"frz_ind": true`)
12. `POST /api/v1/todos/{seq_no}/freeze` — **Unfreeze** (body: `"frz_ind": false`)
13. `POST /api/v1/todos/{seq_no}/delete` — **Delete** — clean up

---

## Endpoint Reference

### Pre-edit Gets (open task only — used before edit screens open)

| Route | Name | Key input |
|---|---|---|
| `POST /api/v1/todos/{seq_no}` | GetBySeqNo | `seq_no` path param |
| `POST /api/v1/todos/get/by-booking` | GetByBooking | `bkg_no` |
| `POST /api/v1/todos/get/by-quote` | GetByQuote | `quote_no` |
| `POST /api/v1/todos/get/by-tourseries-departure` | GetByTourSeriesDeparture | `tour_series_code` + `dep_date` |
| `POST /api/v1/todos/get/by-task-source` | GetByTaskSource | exactly one of: `travel_pnr_no`, `seq_no_charges`, `seq_no_acct_notes`, `itinerary_no` |

### Mutations

| Route | Name | Notes |
|---|---|---|
| `POST /api/v1/todos` | Create | Upsert: finds open task for same context; returns `201` (new), `200` (existing), or `204` (no change) |
| `POST /api/v1/todos/{seq_no}/update` | Update | Requires `updated_on` for concurrency check |
| `POST /api/v1/todos/{seq_no}/delete` | Delete | Hard delete |
| `POST /api/v1/todos/{seq_no}/freeze` | Freeze / Unfreeze | `frz_ind: true` freezes, `false` unfreezes; 422 if already frozen |
| `POST /api/v1/todos/{seq_no}/complete` | Complete | 422 if already done |
| `POST /api/v1/todos/{seq_no}/undo-complete` | UndoComplete | Clears `done_by` / `done_on` |

### List endpoints (paginated)

All list endpoints accept `page_no` (default 1) and `page_size` (default 50, max 200). Response includes `has_next_page`.

| Route | Name | Required |
|---|---|---|
| `POST /api/v1/todos/list/by-assignee` | ListByAssignee | `assigned_to_user_code` |
| `POST /api/v1/todos/list/by-tourseries-departure` | ListByTourSeriesDeparture | `tour_series_code` + `dep_date` (or range) |
| `POST /api/v1/todos/list/by-booking` | ListByBooking | `bkg_no` |
| `POST /api/v1/todos/list/by-quote` | ListByQuote | `quote_no` |
| `POST /api/v1/todos/list/by-campaign` | ListByCampaign | `campaign_code` |
| `POST /api/v1/todos/list/by-client` | ListByClient | `account_code_client` |
| `POST /api/v1/todos/list/by-supplier` | ListBySupplier | `supplier_code` |
| `POST /api/v1/todos/list/by-task-source` | ListByTaskSource | exactly one task-source field |

All list endpoints support optional filters: `done_ind` (true/false/omit), `include_frozen` (default false), and date range fields where applicable.

### Aggregate / Summary

| Route | Name | Required |
|---|---|---|
| `POST /api/v1/todos/summary/by-user` | SummaryByUser | `assigned_to_user_code` — counts due-today, overdue, WIP, completed-today (split by priority) |
| `POST /api/v1/todos/summary/by-context` | SummaryByContext | exactly one of: `booking_no`, `quote_no`, `account_code_client`, `supplier_code`, or (`tour_series_code` + `dep_date`) |

---

## Common Error Responses

All errors follow RFC 9457 Problem Details format.

| Status | Scenario |
|---|---|
| `400 Bad Request` | Missing required field, invalid pagination, multiple task-source fields |
| `403 Forbidden` | `tenant_id` in request body does not match JWT claim |
| `404 Not Found` | `seq_no` does not exist |
| `409 Conflict` | Concurrency conflict — `updated_on` in request is older than the DB record (StrictMode only) |
| `422 Unprocessable Entity` | Business rule violation — task already completed, record already frozen |
| `429 Too Many Requests` | Rate limit exceeded (100 requests / 60 s per tenant by default) |

### Disable StrictMode for testing

To avoid `409 Conflict` responses during exploratory testing, set in `opsettings.json`:

```json
"ConcurrencyCheck": {
  "StrictMode": false
}
```

No restart needed — the setting is hot-reloadable via `IOptionsSnapshot<ConcurrencySettings>`.

---

## Wire Format Notes

- All field names are **snake_case** (e.g. `seq_no`, `due_date`, `assigned_to_user_code`).
- Dates: `"2025-09-15"` (ISO 8601 date only — `DateOnly`).
- Datetimes: `"2025-09-15T08:30:00+01:00"` (ISO 8601 with offset — `DateTimeOffset`).
- Boolean indicators: `true` / `false` (JSON booleans, not `"Y"`/`"N"` or `1`/`0`).
- `account_code_client` is a **string** — leading zeros are significant.
