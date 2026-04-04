# Nova Project — Conversation Context

Paste this file at the start of a new chat to resume the architecture/build conversation.

---

## What This Project Is

A greenfield .NET 10 commercial SaaS platform built as microservices (10+ API services, one per domain).
Bookings, financials, stateful workflows. Multi-tenant. Per-tenant separate database.

Working directory: `/Users/rajeevjha/Library/Mobile Documents/com~apple~CloudDocs/dhruv-v2024/dotnet/novadhruv`

Solution file: `novadhruv.slnx` (new .NET 10 format — not `.sln`)

---

## Current Build State

All foundation projects built and passing. Aspire AppHost added. snake_case JSON serialisation confirmed. Problem Details (RFC 9457) global exception handler added. Reference endpoint and developer guide created.

| Project | Path | Status |
|---|---|---|
| `Nova.Shared` | `src/shared/Nova.Shared/` | Built, verified |
| `Nova.Shared.Web` | `src/shared/Nova.Shared.Web/` | Built, verified |
| `Nova.Shell.Api` | `src/services/Nova.Shell.Api/` | Built, verified |
| `Nova.AppHost` | `src/host/Nova.AppHost/` | Built, verified |

Postman collection and mock server files: `planning/postman/`

**Next: Task 5 — Add API versioning (`Asp.Versioning.Http`, URL segment `/api/v1/`)**

### Recent changes (this session)

- `HelloWorldEndpoint.cs` — fixed `DateTime.UtcNow` → `DateTimeOffset.UtcNow`; changed anonymous object to typed `HelloWorldResponse` record (sets the team pattern)
- `EchoEndpoint.cs` — new reference POST endpoint showing snake_case binding, `TypedResults.ValidationProblem` (400), `TypedResults.Problem` (404), deliberate throw (500), `TypedResults.Ok` with typed record
- `planning/ainotes/nova-shell-api-guide.md` — new developer guide: middleware pipeline order, how to add an endpoint, snake_case rules, all Problem Details return patterns, tenant-aware query pattern, standard request context fields, audit columns, clone-to-new-service checklist
- `planning/ainotes/running-and-testing.md` — fixed endpoint table (`correlation_id` snake_case, `/echo` added, `/health/mysql` → `/health/mariadb`); added Problem Details testing section

---

## Infrastructure Improvement Task List

These tasks were agreed and are being implemented one at a time. After each task the user runs Nova.Shell.Api and confirms endpoints before proceeding.

| # | Task | Status |
|---|---|---|
| 1 | Add .NET Aspire AppHost project | **Completed** |
| 1b | Configure snake_case JSON serialisation | **Completed** |
| 2 | Add Problem Details and global exception handler | **Completed** |
| 3 | Add input validation convention + RequestContext | **Completed** |
| 4 | Add standard pagination contract | **Completed** |
| 5 | Add API versioning | Pending |
| 6 | Add HttpClient resilience via Microsoft.Extensions.Http.Resilience | Pending |
| 7 | Add soft-delete filter convention to ISqlDialect | Pending |
| 8 | Add per-tenant rate limiting middleware | Pending |
| 9 | Add Redis cache implementation (ICacheService) | Pending |
| 10 | Add distributed locking (IDistributedLockService) | Pending |
| 11 | Add service-to-service authentication | Pending |
| 12 | Add DbUp database migration strategy | Pending |
| 13 | Implement outbox relay | Pending |

---

## Planning & Reference Files

| File | Purpose |
|---|---|
| `planning/ainotes/nova-shell-api-guide.md` | **Dev team guide** — endpoint patterns, error handling, cloning to a new service |
| `planning/ainotes/architecture-design-prompt.md` | Original design brief (discussion only, no code) |
| `planning/ainotes/codegen-shell-prompt.md` | Code-gen prompt — paste into new chat + append cipher.cs |
| `planning/ainotes/acceptance-criteria.md` | Verification checklist — give to Claude Code to run against the repo |
| `planning/ainotes/conversation-context.md` | This file |
| `planning/ainotes/running-and-testing.md` | CLI commands, VS Code debug, Postman testing guide |
| `planning/postman/Nova.Shell.Api.mock.postman_collection.json` | Postman mock server collection |
| `planning/postman/MockServer-Setup.md` | Instructions for setting up the Postman mock |

---

## Repo & Project Structure

```
novadhruv/
├── novadhruv.slnx
├── .vscode/
│   └── launch.json                     ← VS Code debug configs (3 profiles)
└── src/
    ├── host/
    │   └── Nova.AppHost/               ← Aspire AppHost (dev orchestration only)
    │       ├── Nova.AppHost.csproj     ← Aspire.Hosting.AppHost 13.2.1, no workload needed
    │       └── Program.cs              ← registers Nova.Shell.Api as "shell"
    │
    ├── shared/
    │   ├── Nova.Shared/                    ← pure .NET (IHostApplicationBuilder)
    │   │   ├── Nova.Shared.csproj
    │   │   ├── Caching/
    │   │   │   ├── CacheProfile.cs
    │   │   │   ├── CacheSettings.cs
    │   │   │   └── ICacheService.cs
    │   │   ├── Configuration/
    │   │   │   ├── AppSettings.cs
    │   │   │   ├── ConfigurationExtensions.cs   ← AddNovaConfiguration
    │   │   │   ├── OpsSettings.cs
    │   │   │   └── OpsSettingsValidator.cs
    │   │   ├── Data/
    │   │   │   ├── DbConnectionFactory.cs
    │   │   │   ├── DbType.cs
    │   │   │   ├── IDbConnectionFactory.cs
    │   │   │   ├── ISqlDialect.cs
    │   │   │   ├── MsSqlDialect.cs
    │   │   │   ├── MySqlDialect.cs
    │   │   │   ├── PostgresDialect.cs
    │   │   │   └── SafeReaderExtensions.cs
    │   │   ├── Logging/
    │   │   │   ├── LoggingWindow.cs
    │   │   │   ├── SerilogSetupExtensions.cs    ← AddNovaLogging
    │   │   │   └── TimeWindowLevelEvaluator.cs
    │   │   ├── Messaging/Outbox/
    │   │   │   └── OutboxMessage.cs
    │   │   ├── Observability/
    │   │   │   └── OtelSetupExtensions.cs       ← AddNovaOpenTelemetry (base only)
    │   │   ├── Security/
    │   │   │   ├── cipher.cs                    ← verbatim, do not modify
    │   │   │   ├── CipherService.cs
    │   │   │   └── ICipherService.cs
    │   │   └── Tenancy/
    │   │       ├── TenantContext.cs
    │   │       ├── TenantRecord.cs
    │   │       └── TenantRegistry.cs
    │   │
    │   ├── Nova.Shared/                    ← pure .NET (continued)
    │   │   ├── Requests/
    │   │   │   ├── RequestContext.cs            ← base record for all POST/PATCH request bodies
    │   │   │   ├── PagedRequest.cs              ← extends RequestContext with PageNumber, PageSize, Skip
    │   │   │   └── PagedResult.cs               ← generic PagedResult<T> with From() factory
    │   │   └── Validation/
    │   │       ├── RequestContextValidator.cs   ← Validate() + TenantMatches()
    │   │       └── PagedRequestValidator.cs     ← validates PageNumber >= 1, PageSize 1–100
    │   │
    │   └── Nova.Shared.Web/                ← ASP.NET Core (FrameworkReference)
    │       ├── Nova.Shared.Web.csproj
    │       ├── Auth/
    │       │   └── JwtSetupExtensions.cs        ← AddNovaJwt
    │       ├── Middleware/
    │       │   ├── CorrelationIdMiddleware.cs
    │       │   └── TenantResolutionMiddleware.cs
    │       ├── Observability/
    │       │   └── WebOtelExtensions.cs          ← AddNovaWebInstrumentation
    │       ├── Errors/
    │       │   └── ProblemDetailsSetupExtensions.cs ← AddNovaProblemDetails / UseNovaProblemDetails
    │       ├── Serialisation/
    │       │   └── JsonSetupExtensions.cs        ← AddNovaJsonOptions (snake_case wire format)
    │       └── Tenancy/
    │           └── TenancyExtensions.cs          ← AddNovaTenancy
    │
    └── services/
        └── Nova.Shell.Api/
            ├── Nova.Shell.Api.csproj
            ├── Program.cs
            ├── appsettings.json
            ├── opsettings.json
            ├── Properties/launchSettings.json
            ├── Endpoints/
            │   ├── HelloWorldEndpoint.cs
            │   ├── TestDbMsSqlEndpoint.cs
            │   ├── TestDbPostgresEndpoint.cs
            │   └── TestDbMySqlEndpoint.cs
            └── HealthChecks/
                ├── MsSqlHealthCheck.cs
                ├── PostgresHealthCheck.cs
                └── MySqlHealthCheck.cs
```

---

## Aspire AppHost — Key Facts

- **Aspire version**: `Aspire.Hosting.AppHost 13.2.1` (the .NET 10 era version)
- **No workload install needed** — ships purely via NuGet. `dotnet workload install aspire` is deprecated.
- **Do NOT set** `<IsAspireHost>true</IsAspireHost>` — that is a deprecated workload-era property, causes build error NETSDK1228.
- **Do NOT use** `AddServiceDefaults()` — Nova.Shared is the canonical cross-cutting library. ServiceDefaults would conflict with Serilog, OTel, health check setup.
- **AppHost is dev-only** — production uses Docker Compose / K8s. AppHost just runs everything locally.
- Adding a new domain service to AppHost: add `ProjectReference` with `IsAspireProjectResource`, then `builder.AddProject<Projects.Nova_Xyz_Api>("xyz")` in Program.cs.

**To run via AppHost (preferred for multi-service dev):**
```bash
export ENCRYPTION_KEY=your-dev-key
dotnet run --project src/host/Nova.AppHost
```

**To run Shell API standalone (still works):**
```bash
export ENCRYPTION_KEY=your-dev-key
dotnet run --project src/services/Nova.Shell.Api
```

---

## All Architectural Decisions Made

### Databases
- **MSSQL**: legacy/existing clients. Retiring in 2–3 years. Existing schema, cannot change.
- **Postgres**: all new/greenfield clients. Clean schema.
- **MySQL/MariaDB**: supported. Existing MySQL clients retained. Recent stable MariaDB is fully compatible — `MySqlConnector` works against both without code changes.
- **One database per tenant** — separate DB per client. Not a shared DB with tenant_id row filtering.
- MSSQL "databases" (e.g. `sales97`, `presets`) map to Postgres/MySQL **schemas** within one tenant DB — `schema.table` notation for both.
- Latest versions: MSSQL 2022, Postgres 16+, MySQL 8+ / MariaDB 10.6+ LTS.
- MySQL driver: `MySqlConnector` v2 (community, not Oracle's `MySql.Data`).

### Primary Keys
- MSSQL: `SeqNo INT IDENTITY(1,1)` — existing convention, kept as-is
- MySQL/MariaDB: `SeqNo INT AUTO_INCREMENT` — same column name as MSSQL, DB-generated
- Postgres: `id uuid` — UUID v7 via `Guid.CreateVersion7()`, app-generated
- All API DTOs use `string` for IDs. Infrastructure parses to `int` (MSSQL/MySQL) or `Guid` (Postgres) based on `DbType`.

### Standard Audit Columns (every table, after domain columns, in this order)
```
frz_ind     bit / boolean           NOT NULL DEFAULT 0    -- soft delete flag
created_on  datetime2 / timestamptz NOT NULL
created_by  nvarchar(10) / varchar(10) NOT NULL
updated_on  datetime2 / timestamptz NULL
updated_by  nvarchar(10) / varchar(10) NULL
updated_at  nvarchar(45) / varchar(45) NULL               -- client IP address, NOT a datetime
```
- `frz_ind = 1` = frozen/soft-deleted. All queries filter `WHERE frz_ind = 0`.
- Batch jobs hard-delete frozen records. API never hard-deletes.

### Tenancy Model
- `TenantContext` (scoped, per-request): `{ TenantId, ConnectionString (encrypted), DbType, SchemaVersion }`
- `DbType` enum: `MsSql | Postgres | MySql`
- Resolved from JWT claim `tenant_id` → `TenantRegistry` lookup → `TenantContext` stored in `HttpContext.Items`
- `TenantRegistry` is singleton, loaded from `appsettings.json` `Tenants[]` array
- Downstream code injects `TenantContext` via DI (not via `HttpContext.Items` directly)

### Encryption / Decryption
- `cipher.cs`: placed verbatim in `Nova.Shared/Security/`. Never modified.
- `CipherService`: thin wrapper. `ICipherService { Encrypt, Decrypt }`.
- `ENCRYPTION_KEY` read exclusively from `Environment.GetEnvironmentVariable("ENCRYPTION_KEY")`. Never from config. Throws on startup if missing.
- Decryption called only in `DbConnectionFactory` and `JwtSetupExtensions`. No other layer calls `ICipherService`.

### ISqlDialect
- `TableRef("sales97", "pointer")` → `"sales97.dbo.pointer"` (MSSQL), `"sales97.pointer"` (Postgres/MySQL)
- `ReturningIdClause()` → `"OUTPUT INSERTED.SeqNo"` (MSSQL), `"RETURNING id"` (Postgres), `"LAST_INSERT_ID()"` (MySQL)
- `BooleanLiteral(bool)` → `"1"/"0"` (MSSQL/MySQL) or `"true"/"false"` (Postgres)
- `PaginationClause(skip, take)`

### Configuration — Two Files
**`appsettings.json`** (application config, restart required):
- `DiagnosticConnections` — MSSQL + Postgres connection strings (encrypted)
- `Tenants[]` — tenant registry
- `Jwt` — issuer, audience, encrypted secret key
- `OpenTelemetry` — service name, OTLP endpoint
- `Kestrel:Endpoints:Http:Url` — listen URL for production/Docker (`http://0.0.0.0:5100`)

**`opsettings.json`** (operational config, hot-reloadable):
- `Logging` — DefaultLevel, EnableRequestResponseLogging, EnableDiagnosticLogging, Windows[]
- `Caching` — GloballyEnabled, EmergencyDisable, DryRunMode, Profiles, EndpointExclusions

Hot-reload: validate before applying. On failure: retain last-known-good, log warning.

### Logging (Serilog)
- Two file sinks: `logs/audit-.log` (Info+), `logs/debug-.log` (Debug+, only when enabled)
- `AddNovaLogging` targets `IHostApplicationBuilder`. Uses `builder.Services.AddSerilog(dispose: true)` (not `builder.Host.UseSerilog()`).
- Time-window logging: `TimeWindowLevelEvaluator` checks current UTC time against `OpsSettings.Logging.Windows[]`

### OpenTelemetry
- **`AddNovaOpenTelemetry`** (Nova.Shared): runtime metrics + OTLP exporter + `ActivitySource("Nova.Shell")` registered as singleton. Targets `IHostApplicationBuilder`.
- **`AddNovaWebInstrumentation`** (Nova.Shared.Web): adds `AddAspNetCoreInstrumentation` and `AddHttpClientInstrumentation` on top, using `ConfigureOpenTelemetryTracerProvider` / `ConfigureOpenTelemetryMeterProvider`.
- Resource: `service.name`, `service.version`, `deployment.environment`.
- When running under Aspire AppHost, the dashboard acts as the local OTel backend — OTLP export routes there automatically.

### Why Nova.Shared vs Nova.Shared.Web Split
- `Nova.Shared` must work for console/worker hosts as well as web API hosts — extension methods use `IHostApplicationBuilder` (common interface for both).
- `Nova.Shared.Web` requires ASP.NET Core types (`HttpContext`, `RequestDelegate`, `JwtBearer`) — uses `<FrameworkReference Include="Microsoft.AspNetCore.App" />`.
- Any future console app or worker service references only `Nova.Shared`.
- Any web API project references both.

### JSON Serialisation
- All JSON request and response bodies use **snake_case** on the wire (e.g. `tenant_id`, `correlation_id`, `page_size`).
- Configured via `ConfigureHttpJsonOptions` with `JsonNamingPolicy.SnakeCaseLower` — applies to all Minimal API responses and request binding.
- `DictionaryKeyPolicy` also set to snake_case — dictionary keys serialise consistently.
- `PropertyNameCaseInsensitive = true` — incoming requests bind regardless of casing (camelCase or snake_case both accepted during development/testing).
- Extension method: `AddNovaJsonOptions()` in `Nova.Shared.Web/Serialisation/JsonSetupExtensions.cs`.
- No per-endpoint configuration needed — the policy is global.

### Pagination Contract

All list/search endpoints use `PagedRequest` (extends `RequestContext`) and `PagedResult<T>`.

`PagedRequest` adds: `PageNumber` (default 1), `PageSize` (default 25), `Skip` (computed = `(PageNumber-1) * PageSize`).

`PagedResult<T>` fields: `Items`, `TotalCount`, `PageNumber`, `PageSize`, `TotalPages` (computed), `HasNextPage` (computed), `HasPreviousPage` (computed). Built via `PagedResult<T>.From(rows, totalCount, pageNumber, pageSize)`.

`PagedRequestValidator.Validate(request)` — enforces `PageNumber >= 1` and `1 <= PageSize <= 100` (MaxPageSize). Returns `Dictionary<string, string[]>` (same pattern as `RequestContextValidator`).

SQL pattern: `dialect.PaginationClause(request.Skip, request.PageSize)` — generates `OFFSET/FETCH` (MSSQL) or `LIMIT/OFFSET` (Postgres/MySQL).

Validation order for paginated endpoints: `RequestContextValidator.Validate` → `TenantMatches` → `PagedRequestValidator.Validate` → domain validation.

### Input Validation Convention

Every POST and PATCH request record inherits `Nova.Shared.Requests.RequestContext` — a base record carrying the 7 fields the frontend `apiClient` auto-injects on every request (`tenant_id`, `company_id`, `branch_id`, `user_id`, `browser_locale`, `browser_timezone`, `ip_address`).

Validation order in every handler (mandatory):
1. `RequestContextValidator.Validate(request)` — validates required standard fields → 400 `ValidationProblem` if any missing
2. `RequestContextValidator.TenantMatches(request, tenantContext)` — body `tenant_id` vs JWT-resolved tenant → 403 if mismatch
3. Domain-specific field validation → 400 `ValidationProblem`
4. Business rule violations → 422 `Problem`

`RequestContextValidator` lives in `Nova.Shared.Validation` — pure .NET, no ASP.NET Core types. Returns `Dictionary<string, string[]>` — the exact type `TypedResults.ValidationProblem(errors)` expects. No extra mapping needed.

`RequestContext` is not `sealed` so domain request records can inherit it cleanly.

### Problem Details (RFC 9457 Error Responses)

- All error responses (4xx and 5xx) return RFC 9457 Problem Details JSON.
- Extension method: `AddNovaProblemDetails()` (services) + `UseNovaProblemDetails()` (pipeline) in `Nova.Shared.Web/Errors/ProblemDetailsSetupExtensions.cs`.
- `UseNovaProblemDetails()` must be the **first** middleware in the pipeline — wraps `UseExceptionHandler()` + `UseStatusCodePages()`.
- No stack traces exposed to clients.
- Every error response is enriched with two fields:
  - `correlation_id` — read from `HttpContext.Items["X-Correlation-ID"]` (set by `CorrelationIdMiddleware`)
  - `trace_id` — from `Activity.Current?.Id` or `HttpContext.TraceIdentifier`
- `instance` is suppressed (null) to avoid leaking internal server paths.
- Pipeline order: `UseNovaProblemDetails` → `CorrelationIdMiddleware` → `UseAuthentication` → `UseAuthorization` → `TenantResolutionMiddleware`.

### HTTP Method Convention

- **All data-retrieval endpoints use POST** (not GET) — keeps filters out of URLs, server logs, and browser history.
- **PATCH** for partial updates (save booking, update client details, etc.).
- GET reserved for parameter-free endpoints only (`/health`, `/hello-world`).

### Standard Context Fields (Auto-Injected by Frontend)

Every POST/PATCH request from the frontend `apiClient` (`src/services/apiNovaDhruvUxConfig.ts`) includes these 7 fields automatically in the JSON body:

**API Context:**
- `tenant_id` — must be validated against JWT-resolved `TenantContext.TenantId` (mismatch = 403)
- `company_id`, `branch_id`, `user_id` — current user's context

**Client Context:**
- `browser_locale` — `navigator.language` (e.g. `"en-GB"`)
- `browser_timezone` — IANA timezone (e.g. `"Europe/London"`)
- `ip_address` — client IP from ipify.org on startup (may be null)

**Backend rules:**
- Define `RequestContext` record in `Nova.Shared` as the base for all request bodies.
- `ip_address` from body = stored in `updated_at` audit column (client-reported). Server also reads `X-Forwarded-For` for security audit logging — do not use body IP for security decisions.
- `browser_locale` / `browser_timezone` stored for display preferences, not business logic.

### Code Rules
- No EF Core. Dapper + explicit SQL. `Microsoft.Data.SqlClient` + `Npgsql`.
- No MediatR. No AutoMapper. No generic repositories.
- Minimal API style (not controllers).
- Always `DateTimeOffset.UtcNow` — never `DateTime.Now`.
- All queries parameterised — no string interpolation in SQL.
- English UK spelling throughout all identifiers.
- XML `<summary>` on all public members in `Nova.Shared`.
- `MySqlConnector` (not `MySql.Data`) — supports MySQL 8+ and MariaDB 10.6+ LTS interchangeably.

### Console Application Mode
- `RUN_AS_CONSOLE=true` env var or `--console` arg → `UseConsoleLifetime()`.
- Verbose startup output: config loaded, DB pings (MSSQL, Postgres, MySQL), tenant count, OTel endpoint.

### Development Tooling
- `.vscode/launch.json` at repo root — three VS Code debug configs: HTTP, console mode, attach to process.
- `ENCRYPTION_KEY` must be set in the shell before launching (inherited via `${env:ENCRYPTION_KEY}`).
- Run commands and Postman setup documented in `planning/ainotes/running-and-testing.md`.

---

## Program.cs Startup Order (Nova.Shell.Api)

```csharp
builder.AddNovaConfiguration();          // Nova.Shared — loads appsettings + opsettings
builder.AddNovaLogging();                // Nova.Shared — Serilog
builder.Services.AddNovaTenancy();       // Nova.Shared.Web — TenantRegistry + scoped TenantContext
builder.AddNovaOpenTelemetry();          // Nova.Shared — base OTel (runtime metrics, OTLP, ActivitySource)
builder.AddNovaWebInstrumentation();     // Nova.Shared.Web — ASP.NET Core instrumentation
builder.AddNovaJwt();                    // Nova.Shared.Web — JWT bearer
builder.Services.AddNovaJsonOptions();   // Nova.Shared.Web — snake_case JSON wire format
builder.Services.AddNovaProblemDetails(); // Nova.Shared.Web — RFC 9457 error responses
builder.Services.AddHealthChecks()...;  // health checks
// build
app.UseNovaProblemDetails();             // first — catches all downstream exceptions
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantResolutionMiddleware>();
// map endpoints
```

---

## Shell API Endpoints

| Endpoint | Auth | DB | Notes |
|---|---|---|---|
| `GET /hello-world` | None | None | Returns `message`, `timestamp`, `correlation_id` (snake_case) |
| `GET /test-db/mssql` | None | DiagnosticConnections:MsSql | `SELECT code, value FROM sales97.dbo.pointer` |
| `GET /test-db/postgres` | None | DiagnosticConnections:Postgres | `SELECT code, value FROM sales97.pointer` |
| `GET /test-db/mysql` | None | DiagnosticConnections:MySql | `SELECT code, value FROM sales97.pointer` |
| `GET /health` | None | All three | Aggregate health check |
| `GET /health/mssql` | None | MsSql | `SELECT 1` |
| `GET /health/postgres` | None | Postgres | `SELECT 1` |
| `GET /health/mysql` | None | MySql | `SELECT 1` |

Listen port: `5100` (development: `launchSettings.json`; production: `appsettings.json` Kestrel config)

---

## Key Technical Patterns to Preserve

- `using NovaDbType = Nova.Shared.Data.DbType;` — alias required in files that also use `System.Data` to avoid `DbType` ambiguity.
- Table references always built via `ISqlDialect.TableRef()` — never string interpolation.
- `((IConfiguration)builder.Configuration).Bind(appSettings)` — cast required in pure .NET context where `builder.Configuration` is `IConfigurationManager`, not `IConfiguration`.
- `OpsSettingsWatcher` (hosted service) monitors `IOptionsMonitor<OpsSettings>.OnChange` and maintains last-known-good.
- `TenancyExtensions.ContextItemKey` constant shared between `TenancyExtensions` (DI wiring) and `TenantResolutionMiddleware` (writer).

---

## Caching (Designed, Not Yet Implemented)

- Three layers: HTTP response cache → `IMemoryCache` → Redis
- `ICacheService` skeleton only (no implementation yet)
- Cache keys tenant-prefixed: `{tenantId}:{profile}:{route}:{queryFingerprint}`
- Stampede protection via Redis distributed lock on cache miss
- Redis connection config not yet added to appsettings

---

## Messaging / Outbox (Designed, Not Yet Implemented)

- Transactional outbox table in each tenant DB — written in same transaction as business data
- RabbitMQ async messaging; `tenant_id` in message header (not routing key)
- Inbox table for consumer idempotency
- `OutboxMessage` record exists in `Nova.Shared` — no relay implementation yet
- Outbox relay decision made: **hosted service within each API** (not a separate relay service)

---

## Open Questions

- Redis connection configuration (host, auth) — not yet added to appsettings
- Exact MSSQL legacy column name mapping (depends on existing schema review)
- Master tenant registry DB design (currently config-file based)
