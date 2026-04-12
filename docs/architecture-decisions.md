# Nova Platform — Architecture & Design Decisions

Comprehensive reference for all architectural choices, conventions, and rationale across the Nova microservices platform.

---

## Contents

1. [Platform Overview](#1-platform-overview)
2. [Project Structure](#2-project-structure)
3. [Multi-Tenancy](#3-multi-tenancy)
4. [Database & Multi-Engine Support](#4-database--multi-engine-support)
5. [Data Access (Dapper + SQL Dialect)](#5-data-access-dapper--sql-dialect)
6. [Schema & Column Conventions](#6-schema--column-conventions)
7. [Primary Keys](#7-primary-keys)
8. [Transactions](#8-transactions)
9. [UTC Timestamps](#9-utc-timestamps)
10. [API Design Conventions](#10-api-design-conventions)
11. [Request Context (Auto-Injected Fields)](#11-request-context-auto-injected-fields)
12. [Validation Order](#12-validation-order)
13. [Error Handling (RFC 9457)](#13-error-handling-rfc-9457)
14. [Date & Time Handling](#14-date--time-handling)
15. [Security & Encryption](#15-security--encryption)
16. [JWT Authentication](#16-jwt-authentication)
17. [Service-to-Service Auth (Internal JWT)](#17-service-to-service-auth-internal-jwt)
18. [Middleware Pipeline Order](#18-middleware-pipeline-order)
19. [Configuration (Two-File Approach)](#19-configuration-two-file-approach)
20. [Logging (Serilog)](#20-logging-serilog)
21. [Observability (OpenTelemetry)](#21-observability-opentelemetry)
22. [Rate Limiting](#22-rate-limiting)
23. [Caching Strategy](#23-caching-strategy)
24. [Messaging & Transactional Outbox](#24-messaging--transactional-outbox)
25. [Database Migrations (DbUp)](#25-database-migrations-dbup)
26. [Distributed Locking (Redis)](#26-distributed-locking-redis)
27. [Health Checks & Diagnostics](#27-health-checks--diagnostics)
28. [Endpoint Structure](#28-endpoint-structure)
29. [Lean Clean Architecture](#29-lean-clean-architecture)
30. [Naming & Language Conventions](#30-naming--language-conventions)
31. [Startup Sequence (Program.cs)](#31-startup-sequence-programcs)
32. [Aspire AppHost (Dev Orchestration)](#32-aspire-apphost-dev-orchestration)
33. [Testing Strategy](#33-testing-strategy)
34. [Key Files Reference](#34-key-files-reference)

---

## 1. Platform Overview

- **.NET 10** commercial SaaS platform with microservices architecture (10+ API services)
- **Domain**: Bookings, financials, stateful workflows — highly transactional
- **Multi-tenancy**: One separate database per tenant (never shared DB with `tenant_id` row filtering)
- **Solution format**: `novadhruv.slnx` (.NET 10 new format, not `.sln`)
- **Dependencies**: Project references during active development, not NuGet packages

---

## 2. Project Structure

```
novadhruv/
├── novadhruv.slnx
├── aspire.config.json
├── planning/
│   └── ainotes/
├── src/
│   ├── host/
│   │   └── Nova.AppHost/             ← Aspire AppHost (dev-only orchestration)
│   ├── shared/
│   │   ├── Nova.Shared/              ← Pure .NET, no ASP.NET Core
│   │   └── Nova.Shared.Web/          ← ASP.NET Core specific
│   └── services/
│       ├── Nova.Shell.Api/           ← Reference/diagnostic service
│       └── Nova.ToDo.Api/            ← Example domain service
```

### Shared Library Split

**Nova.Shared** — Pure .NET, no `Microsoft.AspNetCore.App` reference.
Contains: configuration, data access, tenancy, security (cipher), logging, OTel base, caching skeleton.
Dependencies: Dapper, DB drivers, Serilog, OpenTelemetry core.

**Nova.Shared.Web** — ASP.NET Core specific, requires `Microsoft.AspNetCore.App`.
Contains: JWT auth, middleware (correlation ID, tenant resolution), OTel web instrumentation, JSON serialisation, migrations, problem details, rate limiting, internal auth.

**Why split**: Shared must work for future console/worker services. `HttpContext`, `RequestDelegate`, and other web-only types must not bleed into the pure shared library.

### Dependency Direction

```
API services → Nova.Shared.Web → Nova.Shared
API services → Nova.Shared
```

Never: `Nova.Shared → domain-specific`, `Nova.Shared.Web → domain-specific`, `Domain layer → Nova.Shared`.

### Interface Creation Policy

Create interfaces **only** when multiple implementations are plausible and will be built:

- ✓ `ISqlDialect` — three DB engines (justified)
- ✓ `IDbConnectionFactory` — three DB engines (justified)
- ✓ `ICipherService` — wrapper enabling DI and testing
- ✓ External services: email, SMS, payments, storage (different vendors)
- ✗ Single-use query classes, one-off helpers
- ✗ Generic repositories (`IRepository<T>`)

---

## 3. Multi-Tenancy

### Per-Tenant Database

Each client has a completely separate database instance. No row-level filtering with a shared `tenant_id` column. This is a hard architectural constraint.

### SchemaVersion

Every tenant record carries a `SchemaVersion` string with exactly two valid values:

| Value | Meaning |
|-------|---------|
| `legacy` | Old inherited schema — pre-Nova, typically MSSQL clients not yet migrated |
| `v1` | Schema created under Nova — used by all new/migrated clients (Postgres, MariaDB) |

A tenant moves from `legacy` → `v1` by a client-by-client DB migration. Once migrated, update `SchemaVersion` in the tenant registry (config/opsettings). This aligns with the MSSQL retirement plan (2–3 year horizon).

Services use `SchemaVersion` to branch SQL queries or column/table name mappings where the legacy schema differs from v1.

### TenantContext Resolution

**Flow**: JWT claim `tenant_id` → `TenantRegistry` lookup (singleton, loaded from `appsettings.json` `Tenants[]`) → `TenantContext` stored in `HttpContext.Items`.

**TenantContext** (scoped per-request):
```csharp
public sealed record TenantContext
{
    public required string TenantId { get; init; }
    public required string ConnectionString { get; init; }   // encrypted
    public required DbType DbType { get; init; }
    public required string SchemaVersion { get; init; }      // "legacy" or "v1"
}
```

**Rules**:
- Code injects `TenantContext` via DI, not directly from `HttpContext.Items`
- `TenantResolutionMiddleware` runs **after** `UseAuthentication`/`UseAuthorization` to read validated JWT claim

### BrokerType (Per-Tenant)

`TenantRecord.BrokerType` determines which message broker the outbox relay uses for that tenant:

| Value | Broker |
|-------|--------|
| `RabbitMq` | AMQP publish to exchange with routing key |
| `Redis` | `XADD` to Redis Stream `nova:events:{Exchange}` |

---

## 4. Database & Multi-Engine Support

### Supported Engines

| Engine | Version | Client Library | Use Case |
|--------|---------|----------------|----------|
| MSSQL | 2022 | `Microsoft.Data.SqlClient` v6 | Legacy clients (retiring 2–3 yrs) |
| PostgreSQL | 16+ | `Npgsql` v9 | All new/greenfield clients |
| MySQL / MariaDB | 8+ / 10.6+ LTS | `MySqlConnector` v2 | Existing MySQL clients |

**MSSQL retirement plan**: Client-by-client migration via tenant registry update. No code changes required per migration — only `DbType` and `ConnectionString` update in config.

### DbType Enum

```csharp
public enum DbType { MsSql, Postgres, MySql }
```

### Schema Mapping (MSSQL → Postgres/MySQL)

MSSQL "databases" (e.g., `sales97`) map to Postgres/MySQL **schemas** within one tenant DB. Use `schema.table` notation, never `database.schema.table`.

### Connection Factory

`IDbConnectionFactory` with two methods:
- `CreateForTenant(TenantContext)` — decrypts connection string, creates typed connection
- `CreateFromConnectionString(string connectionString, DbType dbType)` — for diagnostic connections only

Decryption is called **only** in `DbConnectionFactory`. No other code decrypts.

---

## 5. Data Access (Dapper + SQL Dialect)

### No Entity Framework Core

**Absolute prohibition**. No `DbContext`, no `Microsoft.EntityFrameworkCore` packages.

**Rationale**: Multi-tenant per-DB architecture requires direct connection control; EF's lifetime model is incompatible; three DB engines need different SQL syntax; explicit SQL is reviewable.

### SQL Dialect Abstraction (`ISqlDialect`)

Three implementations (`MsSqlDialect`, `PostgresDialect`, `MySqlDialect`) provide DB-specific SQL fragments:

| Method | MSSQL | Postgres | MySQL |
|--------|-------|---------|-------|
| `TableRef(schema, table)` | `sales97.dbo.pointer` | `sales97.pointer` | `sales97.pointer` |
| `ReturningIdClause()` | `OUTPUT INSERTED.SeqNo` | `RETURNING id` | _(use LAST_INSERT_ID after)_ |
| `BooleanLiteral(true)` | `1` | `true` | `1` |
| `ActiveRowsFilter()` | `frz_ind = 0` | `frz_ind = false` | `frz_ind = 0` |
| `SoftDeleteClause()` | `frz_ind = 1` | `frz_ind = true` | `frz_ind = 1` |
| `PaginationClause(skip, take)` | `OFFSET {n} ROWS FETCH NEXT {n} ROWS ONLY` | `LIMIT {n} OFFSET {n}` | `LIMIT {n} OFFSET {n}` |

`ISqlDialect` is resolved as a scoped service based on `TenantContext.DbType`.

### Dapper Usage Rules

- Use `QueryAsync<T>`, `ExecuteAsync`, `ExecuteScalarAsync<T>` with parameterised queries
- **Never** use `$"..."` string interpolation for user values in SQL — always Dapper named parameters (`@ParameterName`)
- `using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);`

### Safe Reader Access (`IDataReader`)

Use ordinal-based access with null guards. Use `SafeReaderExtensions`:
- `GetStringSafe`, `GetInt32Safe`, `GetDateTimeSafe`, `GetBoolSafe`, `GetGuidSafe`
- All return safe defaults on `DBNull` (e.g., `string.Empty`, `0`)
- Resolve ordinals once before the read loop; use `reader.IsDBNull(ord)` for nullable columns

---

## 6. Schema & Column Conventions

### Audit Columns (Every Table, in This Order)

After domain columns, every table has these six in order:

| Column | MSSQL/MySQL Type | Postgres Type | Nullable | Purpose |
|--------|------------------|--------------|----------|---------|
| `frz_ind` | `bit NOT NULL DEFAULT 0` | `boolean NOT NULL DEFAULT false` | No | Soft-delete flag |
| `created_on` | `datetime2 NOT NULL` | `timestamptz NOT NULL` | No | Creation timestamp |
| `created_by` | `nvarchar(10) NOT NULL` | `varchar(10) NOT NULL` | No | User ID who created |
| `updated_on` | `datetime2 NULL` | `timestamptz NULL` | Yes | Last update timestamp |
| `updated_by` | `nvarchar(10) NULL` | `varchar(10) NULL` | Yes | User ID of last update |
| `updated_at` | `nvarchar(45) NULL` | `varchar(45) NULL` | Yes | **Client IP address** (not a datetime) |

**Soft-delete rules**:
- `frz_ind = 1` marks a record as deleted
- All queries filter `WHERE frz_ind = 0`
- API never hard-deletes — only batch jobs hard-delete frozen records
- `updated_at` stores the client-reported IP address, **not** a datetime

---

## 7. Primary Keys

| Engine | Column | Type | Generation |
|--------|--------|------|-----------|
| MSSQL | `SeqNo` | `INT IDENTITY(1,1)` | DB auto-increment |
| MySQL/MariaDB | `SeqNo` | `INT AUTO_INCREMENT` | DB auto-increment |
| Postgres | `id` | `uuid` | `Guid.CreateVersion7()` in application code |

**Identity column naming rule**: Legacy tables use `seqno` (MSSQL: `SeqNo`) as the identity column name, with a few exceptions. All Nova-authored tables use `id`.

**API representation**: All DTOs use `string` for IDs. The infrastructure layer parses to `int` (MSSQL/MySQL) or `Guid` (Postgres).

---

## 8. Transactions

Use `IDbTransaction` when writing to multiple tables or combining a business write with an outbox message.

**Rules**:
- Always pass `transaction: tx` to Dapper calls inside a transaction
- Call `tx.Commit()` explicitly before the `using` block ends
- Re-throw after rollback — global exception handler formats the 500 response
- Keep transactions short — validate and prepare all data **before** opening a transaction
- Single-table writes with no outbox don't require an explicit transaction

---

## 9. UTC Timestamps

**Rule**: Never call `GETDATE()`, `NOW()`, or `CURRENT_TIMESTAMP` in SQL.

**Pattern**: Always pass `DateTimeOffset.UtcNow` as a Dapper parameter. Call `.ToUniversalTime()` on client-supplied values before storing.

**Rationale**: Portable across DB engines; testable (inject fixed timestamps); enables migration replays and batch imports without code changes.

Always set `updated_on` explicitly in UPDATE statements — never via a trigger.

---

## 10. API Design Conventions

### HTTP Method Policy

| Use | When |
|-----|------|
| `MapPost` | All query/search/retrieval endpoints |
| `MapPatch` | Partial updates (save operations) |
| `MapGet` | Parameter-free only: `/health`, `/hello-world` |

**Rationale for POST for retrieval**: Keeps sensitive filters (tenant, date ranges, user) out of URLs, server logs, browser history, and reverse-proxy logs. Request parameters passed in JSON body.

### Serialisation

Global policy: `AddNovaJsonOptions()` sets `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower`.

- All JSON request/response bodies use `snake_case` on the wire
- Case-insensitive binding on incoming requests
- `DictionaryKeyPolicy` also set to `snake_case`
- Never use `[JsonPropertyName]` on individual properties

### Pagination Contract

```csharp
// Request
public sealed record PagedRequest : RequestContext
{
    public int PageNumber { get; init; } = 1;   // >= 1
    public int PageSize { get; init; } = 25;    // 1–100
}

// Response
PagedResult<T>.From(rows, totalCount, pageNumber, pageSize)
// Computes: TotalPages, HasNextPage, HasPreviousPage
```

SQL pattern: two queries (count + data), using `dialect.PaginationClause(request.Skip, request.PageSize)`.

---

## 11. Request Context (Auto-Injected Fields)

Every POST/PATCH request record inherits `RequestContext` with seven fields auto-injected by the frontend `apiClient`:

**API Context** (required):
- `tenant_id` — validate against JWT; 403 on mismatch
- `company_id` — scope queries to company
- `branch_id` — scope queries to branch
- `user_id` — store in audit columns (`created_by`, `updated_by`)

**Client Context** (optional):
- `browser_locale` — `navigator.language` (e.g., `"en-GB"`), stored for display preferences
- `browser_timezone` — IANA timezone (e.g., `"Europe/London"`), stored for display preferences
- `ip_address` — client IP from ipify.org on startup; stored in `updated_at` column; **never used for security decisions**

**IP Address rules**:
- `ip_address` from request body: client-reported, unverified; stored in `updated_at`, never used for rate limiting or geo-blocking
- Server-side IP (`X-Forwarded-For` / `HttpContext.Connection.RemoteIpAddress`): authoritative for audit/security logging and rate limiting

---

## 12. Validation Order

Every POST/PATCH handler must validate in this exact order:

1. **Context field validation**: `RequestContextValidator.Validate(request)` → HTTP 400 if required fields missing
2. **Tenant mismatch check**: `RequestContextValidator.TenantMatches(request, tenantContext)` → HTTP 403 if mismatch
3. **Domain-specific validation**: Custom field checks → HTTP 400 with structured `errors` dict
4. **Business rule violations**: Semantic errors (e.g., can't cancel an invoiced booking) → HTTP 422 Unprocessable Entity

`RequestContextValidator` returns `Dictionary<string, string[]>` — the exact type `TypedResults.ValidationProblem(errors)` expects.

---

## 13. Error Handling (RFC 9457)

**Global handler**: `UseNovaProblemDetails()` must be **first** in the middleware pipeline.

All errors are `application/problem+json`. Every response includes `correlation_id` and `trace_id`. Stack traces are never exposed to clients.

| Scenario | Result |
|----------|--------|
| Validation failure | `TypedResults.ValidationProblem(errors, title: "Validation failed")` — HTTP 400 |
| Tenant mismatch | `TypedResults.Problem(title: "Forbidden", statusCode: 403)` |
| Not found | `TypedResults.Problem(title: "Not found", statusCode: 404)` |
| Business rule violation | `TypedResults.Problem(title: "Cannot process", statusCode: 422)` |
| Unhandled exception | Automatic via `UseNovaProblemDetails()` — HTTP 500, no stack trace |

`instance` field is suppressed (set to `null`) to avoid leaking internal paths.

---

## 14. Date & Time Handling

**Critical rule**: Wrong type choices cause silent data corruption (dates shifted by timezone offsets).

### `DateOnly` — Calendar Dates

- Wire format: `"yyyy-MM-dd"` (no `T`, no offset)
- Use for: booking date, check-in, check-out, any date with no time component
- Displayed as-is regardless of browser locale

### `DateTimeOffset` — UTC Timestamps

- Wire format: `"yyyy-MM-ddTHH:mm:ssZ"` (UTC, `Z` suffix)
- Use for: created-at, event times, audit trail
- Displayed in browser local time — UX must pre-shift back to UTC before sending to API
- `UtcDateTimeOffsetConverter` registered globally in `AddNovaJsonOptions()`
- Server always generates `DateTimeOffset.UtcNow`

### Never Use `DateTime`

Prohibited in request/response records. Ambiguous about timezone (local vs UTC).

### No Per-Property Overrides

Never apply `[JsonConverter]` to individual properties. Never hardcode date formats in SQL queries. Format is a serialisation concern, not a DB concern.

---

## 15. Security & Encryption

### Encryption Strategy

- **`cipher.cs`**: Verbatim, placed in `Nova.Shared/Security/` — **never modified**
- **`CipherService`**: Thin facade implementing `ICipherService { Encrypt, Decrypt }`, singleton
- **`ENCRYPTION_KEY`**: Read **only** in `CipherService` constructor via `Environment.GetEnvironmentVariable("ENCRYPTION_KEY")`. Throws `InvalidOperationException` on startup if missing or empty
- **Decryption call sites**: Only `DbConnectionFactory` and `JwtSetupExtensions`. No other code calls `ICipherService.Decrypt()`
- All connection strings, JWT secrets, broker credentials are encrypted in config files

### SQL Injection Prevention

- All parameterised queries (Dapper named parameters)
- No string interpolation (`$"..."`) in SQL ever

### Cross-Tenant Data Leakage Prevention

- Every query filters by tenant-scoped connection or `tenant_id`
- Request body `tenant_id` validated against JWT-resolved tenant (403 on mismatch)
- Tenant-scoped cache keys (`{tenantId}:{profile}:{route}:{fingerprint}`)
- All logging includes `TenantId`
- Integration tests verify records from another tenant are not visible

---

## 16. JWT Authentication

- **Scheme**: JWT bearer (`Bearer {token}`)
- **Config**: `appsettings.json` → `Jwt` section (`Issuer`, `Audience`, `SecretKey` encrypted)
- **Tenant claim**: `tenant_id` extracted from JWT, validated against request body
- **Extension**: `AddNovaJwt()` from `Nova.Shared.Web` registers bearer scheme

---

## 17. Service-to-Service Auth (Internal JWT)

Separate from user JWT: different signing key, different audience (`nova-internal`), different subject (service name).

- `AddNovaInternalAuth()` registers `InternalJwt` bearer scheme alongside the user scheme
- `ServiceTokenProvider` generates internal JWT at runtime (fast path caches until 30s before expiry)
- `ServiceTokenHandler` (DelegatingHandler) attaches token to outbound calls via `AddNovaInternalHttpClient()`
- Endpoints decorated with `.RequireAuthorization(InternalAuthConstants.PolicyName)` accept only internal tokens
- Config: `appsettings.json` → `InternalAuth` section (`ServiceName`, `SecretKey` encrypted, `TokenLifetimeSeconds`)

---

## 18. Middleware Pipeline Order

Order is critical — must be exactly:

1. `UseNovaProblemDetails()` — **FIRST** — wraps all downstream exceptions in RFC 9457 format
2. `CorrelationIdMiddleware` — generates/reads correlation ID, stores in `HttpContext.Items`
3. `UseAuthentication()` — validates JWT, populates `HttpContext.User`
4. `UseAuthorization()` — runs authorization policies
5. `UseNovaRateLimiting()` — per-tenant rate limiting (after auth so partition key is available)
6. `TenantResolutionMiddleware` — resolves `TenantContext` from JWT claim

Route groups:
- Versioned `/api/v{version:apiVersion}` — rate-limited, requires auth
- Unversioned — health checks, diagnostics, `hello-world` (not rate-limited)

---

## 19. Configuration (Two-File Approach)

### `appsettings.json` — Application Config (Restart Required)

Database connections, tenant registry, JWT settings, OTel endpoints, broker credentials. All sensitive values encrypted.

```json
{
  "DiagnosticConnections": {
    "MsSql":    { "ConnectionString": "ENC:...", "Enabled": true },
    "Postgres": { "ConnectionString": "ENC:...", "Enabled": false },
    "MariaDb":  { "ConnectionString": "ENC:...", "Enabled": false }
  },
  "Tenants": [
    {
      "TenantId":         "BTDK",
      "DisplayName":      "Blixen Tours",
      "DbType":           "MsSql",
      "ConnectionString": "ENC:...",
      "SchemaVersion":    "legacy",
      "BrokerType":       "RabbitMq"
    },
    {
      "TenantId":         "client-b",
      "DisplayName":      "Client B Ltd",
      "DbType":           "Postgres",
      "ConnectionString": "ENC:...",
      "SchemaVersion":    "v1",
      "BrokerType":       "Redis"
    }
  ],
  "Jwt":          { "Issuer": "...", "Audience": "...", "SecretKey": "ENC:..." },
  "InternalAuth": { "ServiceName": "nova-shell", "SecretKey": "ENC:...", "TokenLifetimeSeconds": 300 },
  "OpenTelemetry": { "ServiceName": "Nova.Shell.Api", "OtlpEndpoint": "http://localhost:4317" },
  "RabbitMq":     { "Host": "localhost", "Port": 5672, "Username": "guest", "Password": "ENC:...", "VirtualHost": "/" },
  "Kestrel":      { "Endpoints": { "Http": { "Url": "http://0.0.0.0:5100" } } }
}
```

### `opsettings.json` — Operational Config (Hot-Reload, No Restart)

Logging levels, caching policies, rate limiting, outbox relay tuning.

```json
{
  "Logging": {
    "DefaultLevel": "Information",
    "EnableRequestResponseLogging": false,
    "EnableDiagnosticLogging": false,
    "Windows": [
      { "Name": "peak", "Start": "08:00", "End": "18:00", "Level": "Debug" }
    ]
  },
  "Caching": {
    "GloballyEnabled": true,
    "EmergencyDisable": false,
    "DryRunMode": false,
    "Profiles": { "reference": { "MaxAge": 3600 }, "search": { "MaxAge": 300 } },
    "EndpointExclusions": []
  },
  "RateLimiting": { "Enabled": true, "PermitLimit": 100, "WindowSeconds": 60, "QueueLimit": 0 },
  "OutboxRelay":  { "Enabled": true, "PollingIntervalSeconds": 5, "BatchSize": 50 }
}
```

### Hot-Reload Implementation

- `OpsSettingsWatcher` hosted service subscribes to `IOptionsMonitor<OpsSettings>.OnChange`
- On change: validates via `OpsSettingsValidator` (`IValidateOptions<OpsSettings>`)
- On success: last-known-good reference updated, success logged
- On failure: previous settings retained, warning logged, no crash, no config applied
- Downstream code injects `IOpsSettingsAccessor` (not `IOptions<OpsSettings>` directly)

---

## 20. Logging (Serilog)

### Configuration

All sink decisions live in `Nova.Shared/Logging/SerilogSetupExtensions.cs`. Services call `builder.AddNovaLogging()` and are otherwise unaware of what sinks are active. `Nova.AppHost` owns which infrastructure containers start and injects their endpoints; `AddNovaLogging()` consumes those endpoints.

- Extension: `AddNovaLogging()` targets `IHostApplicationBuilder`; uses `builder.Services.AddSerilog(dispose: true)` — **not** `builder.Host.UseSerilog()`
- Enrichers: `FromLogContext`, correlation ID (from `HttpContext.Items`), tenant ID (from `TenantContext`)

### Sink Pipeline

| Sink | Always active? | Condition |
|------|---------------|-----------|
| Console | Yes | Always |
| `logs/audit-.log` (Info+, rolling daily) | Yes | Always |
| `logs/debug-.log` (Debug+, rolling daily) | No | `opsettings.json Logging.EnableDiagnosticLogging: true` |
| OpenTelemetry OTLP | No | `OTEL_EXPORTER_OTLP_ENDPOINT` env var set (injected by Aspire automatically) |
| Seq | No | `ConnectionStrings:seq` present (injected by Aspire when `Infrastructure:UseSeq: true` in AppHost) |

**Seq** is the primary persistent log store for local development. All services write to a single Seq instance at `http://localhost:5341`. Supports cross-service structured queries (e.g. `tenant_id = 'BTDK' and @Level = 'Error'`). Silently skipped when running outside Aspire (tests, standalone `dotnet run`).

### Log Levels

| Level | Use for |
|-------|---------|
| Trace | Step-by-step diagnostic detail — never in production |
| Debug | Dev-time diagnostic info |
| Information | Normal operation milestones (booking created, payment processed) |
| Warning | Unexpected but recoverable (retry attempt, tenant not found) |
| Error | Failures needing attention — always include exception |
| Critical | Application-level failures (startup failure, data corruption) |

### Structured Logging Rules

Always use message templates — never string interpolation in log calls:

```csharp
// ✓
_logger.LogInformation("Booking {BookingRef} created for tenant {TenantId}", ref, tenantId);
// ✗
_logger.LogInformation($"Booking {ref} created");
```

Never log passwords, tokens, connection strings, PII, or card numbers.

### Time-Window Logging

`TimeWindowLevelEvaluator` checks current UTC time against `OpsSettings.Logging.Windows[]`. Higher verbosity during configured windows (e.g., peak hours). Hot-reloadable via opsettings.

---

## 21. Observability (OpenTelemetry)

### Setup

- **Base** (`AddNovaOpenTelemetry` in `Nova.Shared`): runtime metrics, OTLP exporter, `ActivitySource("Nova.*")` singleton. No ASP.NET Core instrumentation.
- **Web** (`AddNovaWebInstrumentation` in `Nova.Shared.Web`): `AddAspNetCoreInstrumentation`, `AddHttpClientInstrumentation`
- Resource attributes: `service.name`, `service.version`, `deployment.environment`

### Aspire Dashboard

Local development: Aspire dashboard acts as the local OTel backend. OTLP routes automatically — no manual config needed.

### Correlation IDs

- **Header**: `X-Correlation-ID`
- **JSON wire format**: `correlation_id` (snake_case)
- **Generation**: `CorrelationIdMiddleware` generates UUID if absent, passes through if present
- **Propagation**: Forwarded to all outbound HTTP calls
- **Logging**: Added to all log entries via Serilog enricher
- **Problem Details**: Included in every error response

---

## 22. Rate Limiting

- **Type**: Fixed window per request
- **Partition key**: `tenant:{tenantId}` (authenticated) or `ip:{remoteIp}` (anonymous)
- **Scope**: Applied to versioned route group `/api/v1/*`; health checks excluded
- **Response on breach**: HTTP 429, RFC 9457 problem details, `Retry-After` header
- **Hot-reloadable**: Changes in `opsettings.json` apply immediately

---

## 23. Caching Strategy

Three-tier (designed, not yet fully implemented):

1. HTTP response cache (framework-level)
2. In-memory cache (`IMemoryCache`) — instance-scoped
3. Redis cache — shared across instances

### Cache Keys

Always tenant-prefixed: `{tenantId}:{profile}:{route}:{queryFingerprint}` — prevents cross-tenant leakage.

### Cache Profiles

Defined in `opsettings.json` `Caching.Profiles`. Never hard-coded. Per-endpoint application via naming conventions or attributes.

- Never cache transactional endpoints (POST/PATCH mutations)
- Safe to cache: reference data, presets, read-only lookups

### Operational Controls

- `EmergencyDisable: true` — disables globally without redeployment
- `DryRunMode: true` — measures hit/miss without serving from cache (use to test effectiveness)
- `EndpointExclusions[]` — per-endpoint opt-out
- All hot-reloadable via opsettings

### Invalidation

- Event-driven: on business write, invalidate related cache entries
- Time-based: TTL expiration (per profile)
- Manual: admin endpoint to clear specific profiles

---

## 24. Messaging & Transactional Outbox

### Why Outbox

Guarantees atomicity between business data write and event publication. Direct RabbitMQ publish inside a request transaction is forbidden — publish happens after commit.

### Outbox Table

One `nova_outbox` table per tenant database. Key columns:

| Column | Purpose |
|--------|---------|
| `id` | Primary key |
| `tenant_id` | Tenant scoping |
| `aggregate_id` | Business entity ID |
| `event_type` | Event discriminator |
| `payload` | JSON event body |
| `exchange` / `routing_key` | RabbitMQ routing |
| `status` | `pending → processing → sent` (or `failed` after max retries) |
| `retry_count` / `max_retries` | Retry tracking |
| `correlation_id` | Tracing linkage |

### Atomicity Pattern

```csharp
using var tx = connection.BeginTransaction();
await _repo.CreateBookingAsync(request, tx, ct);
await _outbox.EnqueueAsync(outboxMessage, tx, ct);
tx.Commit();
// Publishing happens after commit, in OutboxRelayWorker
```

### Outbox Relay Worker

`BackgroundService` (`OutboxRelayWorker`):
- Polls every `OutboxRelay.PollingIntervalSeconds` (default 5s)
- Processes tenants sequentially; acquires Redis distributed lock per tenant (`nova:outbox-relay:{tenantId}`)
- Up to `BatchSize` (default 50) messages per tenant per cycle
- Config in `opsettings.json` → `OutboxRelay` section (hot-reloadable)

### Idempotency (Consumer Side)

Consuming services maintain `inbox_messages(tenant_id, message_id)` with a unique constraint. Check inbox before processing; duplicate `message_id` → skip. Replay-safe.

---

## 25. Database Migrations (DbUp)

### Two-Stage Safety Pipeline

**Stage 1 — Absolute Prohibitions** (`NeverAllowedDetector`):

Unconditionally blocks `DROP DATABASE`, `DROP SCHEMA`. No config override.

**Stage 2 — Per-Engine Allowlist** (`MigrationPolicyChecker`):

Checks each SQL statement against `migrationpolicy.json`. If a command is not on the list, the script is blocked, a warning is logged, and it stays pending until manually run.

### Default Allowlist

Allowed: `CREATE TABLE`, `CREATE INDEX`, `CREATE VIEW`, `ALTER TABLE ADD`, `INSERT`, `UPDATE`, `DELETE`, `SELECT`

Blocked (require manual run + journal): `DROP TABLE`, `ALTER TABLE DROP`, `ALTER TABLE ALTER`, `TRUNCATE`

### Script Layout

```
{Service}/
└── Migrations/
    ├── MsSql/
    │   ├── V001__CreateOutbox.sql
    │   └── V002__*.sql
    ├── Postgres/
    │   └── V001__*.sql
    └── MariaDb/
        └── V001__*.sql
```

Naming: `V{NNN}__{Description}.sql` — three-digit zero-padded, executed alphabetically.

### Execution Flow

1. For each tenant, pick script folder by `TenantRecord.DbType`
2. Query `SchemaVersions` journal for already-applied scripts
3. Evaluate each pending script through both stages
4. Safe scripts passed to DbUp (run in own transaction)
5. Blocked scripts logged as structured warnings on every startup until resolved

### Handling Blocked Scripts

Run manually on the DB, then journal it:

```sql
INSERT INTO SchemaVersions (ScriptName, Applied)
VALUES ('Nova.Shell.Api.Migrations.MsSql.V003__DropOldColumn.sql', GETDATE());
```

To allow a new command type: add it to `migrationpolicy.json`, redeploy. Script re-evaluated on next startup.

---

## 26. Distributed Locking (Redis)

### Why Redis Locks

`lock(obj)` in C# only works within a single process. Redis-based locks coordinate across containers in multi-instance deployments.

### Mechanism

Acquire: `SET {lockKey} {uniqueToken} NX PX {ttlMillis}` — atomic check-and-set.

Release: Lua script atomically checks token match before DEL:
```lua
if redis.call("GET", KEYS[1]) == ARGV[1] then
    return redis.call("DEL", KEYS[1])
else
    return 0
end
```

TTL must exceed expected operation duration with buffer (recommend 2× minimum).

### Usage Pattern

```csharp
string lockKey = $"tenant:{tenantId}:booking:create:{bookingRef}";
await using IDistributedLock? lk = await _lockService.TryAcquireAsync(
    resource: lockKey,
    expiry: TimeSpan.FromSeconds(30),
    ct: cancellationToken);

if (lk is null)
    return Results.Conflict("Already being processed.");

// Critical section
await _repo.CreateBookingAsync(request, ct);
// DisposeAsync() releases lock automatically
```

### Defence in Depth (Three Layers)

1. Distributed lock — prevents race in the common case
2. Application check — business logic guard (e.g., `BookingExistsAsync`)
3. DB unique constraint — catches edge cases (e.g., lock TTL expired mid-operation)

### Common Mistakes

| Mistake | Consequence |
|---------|------------|
| Checking `null` after critical section | Race condition — the `null` check is the gate |
| Shared lock key across all operations | Different tenants/resources block each other |
| TTL shorter than operation duration | Lock expires, concurrent access possible |
| Nested locks acquired in different orders | Deadlock risk |
| Holding lock across user-facing wait | Lock held too long, contention |

`null` response means both "lock held" and "Redis unavailable" — treated identically.

---

## 27. Health Checks & Diagnostics

### Diagnostic Endpoints

| Route | What it checks |
|-------|---------------|
| `GET /health/mssql` | `SELECT 1` on diagnostic MSSQL connection |
| `GET /health/postgres` | `SELECT 1` on diagnostic Postgres connection |
| `GET /health/mysql` | `SELECT 1` on diagnostic MySQL connection |
| `GET /health` | Aggregate of all three |

Uses `DiagnosticConnections` directly via `IDbConnectionFactory.CreateFromConnectionString(...)` — no tenant context.

### Liveness Endpoint

`GET /hello-world` — parameter-free, no auth required, returns JSON with timestamp and correlation ID.

---

## 28. Endpoint Structure

### File Organisation

```
{Service}/
├── Endpoints/
│   ├── HelloWorldEndpoint.cs      ← one endpoint per file
│   ├── SearchBookingsEndpoint.cs
│   └── CreateBookingEndpoint.cs
└── HealthChecks/
    ├── MsSqlHealthCheck.cs
    ├── PostgresHealthCheck.cs
    └── MySqlHealthCheck.cs
```

### Endpoint Pattern

```csharp
public static class SearchBookingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bookings/search", Handle)
             .RequireAuthorization()
             .WithName("SearchBookings");
    }

    private static async Task<IResult> Handle(
        SearchBookingsRequest request,
        TenantContext tenantContext,
        IDbConnectionFactory connectionFactory,
        ISqlDialect dialect,
        ILogger<SearchBookingsEndpoint> logger,
        CancellationToken cancellationToken)
    {
        // 1. Context validation
        // 2. Tenant mismatch check
        // 3. Domain validation
        // 4. Query / command
        // 5. Return result
    }

    private sealed record SearchBookingsRequest : PagedRequest
    {
        public DateOnly? FromDate { get; init; }
        public DateOnly? ToDate { get; init; }
    }

    private sealed record BookingSummary(string Id, string Ref, DateOnly Date);
}
```

### Rules

- ✓ One endpoint per file
- ✓ Static class with single `Map(RouteGroupBuilder)` method
- ✓ Private nested `sealed record` types for request/response DTOs
- ✓ `private static` handler method
- ✓ `CancellationToken cancellationToken` as last parameter
- ✓ `using IDbConnection connection = ...`
- ✓ No inheritance, no base classes
- ✗ No controllers

---

## 29. Lean Clean Architecture

No EF Core. No MediatR. No AutoMapper. No unnecessary abstractions.

### Layers

| Layer | Contains | Depends On |
|-------|---------|-----------|
| API | Endpoints, route handlers | Application only |
| Application | Orchestration, commands, queries, domain services | Domain only |
| Domain | Entities, aggregates, business rules, value objects | Nothing |
| Infrastructure | Repositories, data access, external service clients | Domain + any technology |

**Lean practices**:
- Repositories only if multiple implementations are plausible
- Queries use direct SQL + DTOs (no repositories required)
- Domain logic stays domain; orchestration stays application
- No layers added "just because Clean Architecture says so"

---

## 30. Naming & Language Conventions

### English (UK) Spelling

All C# identifiers use British English:

| ✓ UK | ✗ US |
|-------|-------|
| `Initialise` | `Initialize` |
| `Colour` | `Color` |
| `Analyse` | `Analyze` |
| `Behaviour` | `Behavior` |
| `Authorise` | `Authorize` |
| `Serialise` | `Serialize` |
| `Cancelled` | `Canceled` |

Exception: third-party method names (`JsonSerializer.Serialize()` is the framework's naming).

### File-Scoped Namespaces

`namespace Nova.Shared.Data;` — not block-scoped `namespace { }`.

### Records for DTOs & Value Objects

Always `record` or `sealed record` with `{ get; init; }` properties.
Examples: `TenantContext`, `TenantRecord`, `OutboxMessage`, `RequestContext`, `PagedResult<T>`.

---

## 31. Startup Sequence (Program.cs)

Canonical order for all Nova API services:

```csharp
// 1. Configuration
builder.AddNovaConfiguration();

// 2. Logging
builder.AddNovaLogging();

// 3. Tenancy
builder.Services.AddNovaTenancy();

// 4. OTel base
builder.AddNovaOpenTelemetry();

// 5. OTel web instrumentation
builder.AddNovaWebInstrumentation();

// 6. JWT auth
builder.AddNovaJwt();

// 7. JSON (snake_case)
builder.Services.AddNovaJsonOptions();

// 8. Problem Details
builder.Services.AddNovaProblemDetails();

// 9. Health checks
builder.Services.AddHealthChecks()
    .AddMsSqlHealthCheck(...)
    .AddPostgresHealthCheck(...)
    .AddMySqlHealthCheck(...);

// 10. Distributed locking
builder.Services.AddNovaDistributedLocking();

// 11. Rate limiting
builder.Services.AddNovaRateLimiting();

// 12. Migrations
builder.AddNovaMigrations();

// 13. Outbox relay
builder.AddNovaOutboxRelay();

// 14. Internal auth (if calling other services)
builder.AddNovaInternalAuth();

// 15. Internal HTTP clients (if calling other services)
builder.Services.AddNovaInternalHttpClient("nova-auth", baseUrl);

WebApplication app = builder.Build();

// Middleware — order is critical
app.UseNovaProblemDetails();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseNovaRateLimiting();
app.UseMiddleware<TenantResolutionMiddleware>();

// Run migrations before accepting requests
await app.RunNovaMigrationsAsync(typeof(Program).Assembly);

// Versioned route group (rate-limited, requires auth)
RouteGroupBuilder v1 = app.MapGroup("/api/v{version:apiVersion}")
    .WithApiVersionSet(...)
    .RequireRateLimiting(...);

HelloWorldEndpoint.Map(v1);
// ... more endpoints

// Unversioned (no rate limiting)
app.MapHealthChecks("/health/mssql", ...);
app.MapGet("/hello-world", ...).AllowAnonymous();

await app.RunAsync();
```

---

## 32. Aspire AppHost (Dev Orchestration)

- **Version**: `Aspire.Hosting.AppHost` 13.2.1 (shipped via NuGet, no workload install needed)
- **Location**: `src/host/Nova.AppHost/`
- **Root config**: `aspire.config.json` → `{ "appHost": { "path": "src/host/Nova.AppHost/Nova.AppHost.csproj" } }`

### Rules

- ✗ Do NOT set `<IsAspireHost>true</IsAspireHost>` (deprecated, causes NETSDK1228)
- ✗ Do NOT call `AddServiceDefaults()` (conflicts with Nova.Shared OTel, logging, health checks)
- ✓ Services added via plain `<ProjectReference>`
- **Dev-only**: AppHost is for local development. Production uses Docker Compose / Kubernetes

### Conditional Infrastructure Flags

Container services are opt-in via `Nova.AppHost/appsettings.json`. The AppHost fails fast at startup if Docker is not running and any container is required.

| Flag | Default | Container | When to enable |
|------|---------|-----------|----------------|
| `Infrastructure:UseRedis` | `true` | Redis | Any service uses caching, distributed locking, or Redis-backed outbox relay |
| `Infrastructure:UseRabbitMQ` | `false` | RabbitMQ | At least one tenant uses `BrokerType: RabbitMq` in any service config |
| `Infrastructure:UseSeq` | `true` | Seq | Always — persistent structured log store for local dev |

All containers use `WithLifetime(ContainerLifetime.Persistent)` — they survive AppHost restarts and preserve data between runs.

### Adding a New Domain Service

```csharp
// Nova.AppHost/Program.cs
var xyz = builder.AddProject<Projects.Nova_Xyz_Api>("xyz");
if (redis is not null) xyz.WithReference(redis).WaitFor(redis);
if (seq   is not null) xyz.WithReference(seq).WaitFor(seq);
```

```xml
<!-- Nova.AppHost/Nova.AppHost.csproj -->
<ProjectReference Include="..\Nova.Xyz.Api\Nova.Xyz.Api.csproj" />
```

Wire `WithReference(seq)` on every new service so logs flow to Seq automatically.

### Running Locally

```bash
export ENCRYPTION_KEY=your-dev-key
aspire run
```

Aspire dashboard starts automatically at the URL printed in the terminal. OTLP traces route to the dashboard without manual config. Seq starts at `http://localhost:5341`.

---

## 33. Testing Strategy

### Coverage Requirements

| Type | Covers |
|------|--------|
| Unit tests | Domain logic only (no I/O) |
| Integration tests | Repositories, DB queries — real DB via TestContainers |
| API tests | Critical endpoints — real HTTP, real DB |
| Tenant isolation tests | Verify cross-tenant data not visible |

### Multi-DB Testing

Any query using `ISqlDialect` is tested against all three target engines (MSSQL, Postgres, MySQL). TestContainers spins up real DB instances. CI/CD pipeline runs all tests.

### Test Isolation Rules

- Each test creates its own data, tears down after
- No shared state between tests
- No execution order dependencies
- Repeatable and fast

---

## 34. Key Files Reference

### Nova.Shared

| File | Purpose |
|------|---------|
| `Security/cipher.cs` | Encryption algorithm — verbatim, never modify |
| `Security/CipherService.cs` | Singleton wrapper; reads `ENCRYPTION_KEY` env var |
| `Data/DbType.cs` | `MsSql | Postgres | MySql` enum |
| `Data/ISqlDialect.cs` | Interface for DB-specific SQL fragments |
| `Data/{MsSql,Postgres,MySql}Dialect.cs` | Three implementations |
| `Data/IDbConnectionFactory.cs` | Creates typed connections |
| `Data/DbConnectionFactory.cs` | Calls `ICipherService.Decrypt()`, creates connections |
| `Tenancy/TenantContext.cs` | Scoped record: `TenantId`, `ConnectionString`, `DbType`, `SchemaVersion` |
| `Tenancy/TenantRecord.cs` | Config record: tenant definition from `appsettings.json` |
| `Tenancy/TenantRegistry.cs` | Singleton: loads tenants from config at startup |
| `Requests/RequestContext.cs` | Base record: 7 auto-injected fields |
| `Requests/PagedRequest.cs` | Extends `RequestContext` with pagination |
| `Validation/RequestContextValidator.cs` | Validates standard fields and tenant match |
| `Messaging/Outbox/OutboxMessage.cs` | Outbox event record |

### Nova.Shared.Web

| File | Purpose |
|------|---------|
| `Auth/JwtSetupExtensions.cs` | `AddNovaJwt()` |
| `Auth/InternalAuth/` | Service-to-service JWT scheme |
| `Middleware/CorrelationIdMiddleware.cs` | Generates/passes correlation ID |
| `Middleware/TenantResolutionMiddleware.cs` | Resolves `TenantContext` from JWT claim |
| `Errors/ProblemDetailsSetupExtensions.cs` | `AddNovaProblemDetails()`, `UseNovaProblemDetails()` |
| `Serialisation/JsonSetupExtensions.cs` | `AddNovaJsonOptions()` (snake_case, UTC dates) |
| `Tenancy/TenancyExtensions.cs` | `AddNovaTenancy()` |
| `Observability/WebOtelExtensions.cs` | `AddNovaWebInstrumentation()` |
| `Migrations/TenantMigrationRunner.cs` | DbUp execution with two-stage safety pipeline |
| `Messaging/OutboxRelayWorker.cs` | Background service polling outbox |
| `Messaging/OutboxRepository.cs` | Dapper queries for outbox table |

### Nova.Shell.Api

| File | Purpose |
|------|---------|
| `Program.cs` | Startup wiring, route registration |
| `appsettings.json` | Application config |
| `opsettings.json` | Operational config (hot-reloadable) |
| `migrationpolicy.json` | DbUp command allowlists per DB engine |
| `Endpoints/HelloWorldEndpoint.cs` | `GET /hello-world` liveness check |
| `Endpoints/TestDb{MsSql,Postgres,MySql}Endpoint.cs` | Diagnostic DB queries |
| `HealthChecks/{MsSql,Postgres,MySql}HealthCheck.cs` | Health check implementations |
| `Migrations/{MsSql,Postgres,MariaDb}/` | Migration script folders |
| `docs/nova-shell-api-guide.md` | Full developer guide — reference implementation |
| `docs/shell-api-running-tests.md` | Test run guide (14/14 passing) |

### Nova.CommonUX.Api (port 5102)

| File | Purpose |
|------|---------|
| `Program.cs` | Startup wiring |
| `appsettings.json` / `opsettings.json` | App and ops config |
| `Endpoints/Auth/` | Login, token, refresh, magic-link, social, 2FA, forgot/reset password |
| `Endpoints/TenantConfigEndpoint.cs` | `POST /tenant-config` — branding and feature flags |
| `Endpoints/MainAppMenusEndpoint.cs` | `POST /novadhruv-mainapp-menus` — nav menus per tenant |
| `Endpoints/AuthDbHealthEndpoint.cs` | `GET /health/db` |
| `Endpoints/RunAuthMigrationsEndpoint.cs` | `POST /admin/migrations/run` |
| `Migrations/{MsSql,Postgres,MariaDb}/` | `nova_auth` DB schema; 2 versions (V001 base, V002 enabled_auth_methods) |

### Nova.Presets.Api (port 5103)

| File | Purpose |
|------|---------|
| `Program.cs` | Startup wiring; serves `/avatars` as static files |
| `appsettings.json` / `opsettings.json` | App and ops config; `AvatarStorage.LocalDirectory` must be a valid OS path |
| `Endpoints/UserProfileEndpoint.cs` | `POST /api/v1/user-profile` |
| `Endpoints/UploadAvatarEndpoint.cs` | `POST /api/v1/user-profile/avatar` (multipart) |
| `Endpoints/StatusOptionsEndpoint.cs` | `POST /api/v1/user-profile/status-options` |
| `Endpoints/UpdateStatusEndpoint.cs` | `PATCH /api/v1/user-profile/status` |
| `Endpoints/ChangePasswordEndpoint.cs` | `POST /api/v1/user-profile/change-password` |
| `Endpoints/ConfirmPasswordChangeEndpoint.cs` | `POST /api/v1/user-profile/confirm-password-change` (anonymous) |
| `Endpoints/BranchesEndpoint.cs` | `POST /api/v1/branches` |
| `Migrations/{MsSql,Postgres,MariaDb}/` | `presets` DB schema |
| `docs/presets-api-running-tests.md` | Test run guide (39/39 passing) |
