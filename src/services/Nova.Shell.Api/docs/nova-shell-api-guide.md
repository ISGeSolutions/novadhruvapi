# Nova.Shell.Api — Developer Guide

This document is the primary reference for developers working on `Nova.Shell.Api` or creating a new Nova API service by cloning this one.

---

## What This Service Is

`Nova.Shell.Api` is the platform shell — a production-grade skeleton that proves all infrastructure works end-to-end (auth, multi-DB tenancy, observability, error handling). It contains no business logic. When creating a domain service (e.g. `Nova.Bookings.Api`), copy this project and delete the diagnostic endpoints.

---

## Project Structure

```
src/services/Nova.Shell.Api/
├── Nova.Shell.Api.csproj
├── Program.cs                  ← startup wiring only — no business logic
├── appsettings.json            ← app config (restart required to pick up changes)
├── opsettings.json             ← ops config (hot-reloadable)
├── Properties/
│   └── launchSettings.json     ← local dev launch profiles
├── Endpoints/
│   ├── HelloWorldEndpoint.cs   ← GET /hello-world — liveness check
│   ├── EchoEndpoint.cs         ← POST /echo — REFERENCE: validation + Problem Details pattern
│   ├── EchoListEndpoint.cs     ← POST /echo/list — REFERENCE: pagination contract pattern
│   ├── HttpPingEndpoint.cs     ← GET /http-ping — REFERENCE: resilient HttpClient pattern
│   ├── TestDbMsSqlEndpoint.cs      ← GET /test-db/mssql — DB connectivity check
│   ├── TestDbPostgresEndpoint.cs
│   ├── TestCacheEndpoint.cs        ← GET/DELETE /test-cache — Redis cache round-trip
│   ├── TestLockEndpoint.cs         ← GET /test-lock — distributed lock round-trip
│   └── TestInternalAuthEndpoint.cs ← GET /test-internal-auth/* — service-to-service auth
├── Migrations/
│   ├── MsSql/    V{NNN}__{Description}.sql  ← embedded SQL scripts for MSSQL tenants
│   ├── Postgres/ V{NNN}__{Description}.sql  ← embedded SQL scripts for Postgres tenants
│   └── MariaDb/  V{NNN}__{Description}.sql  ← embedded SQL scripts for MariaDB tenants
└── HealthChecks/
    ├── MsSqlHealthCheck.cs
    ├── PostgresHealthCheck.cs
    └── MariaDbHealthCheck.cs
```

Each endpoint is a single static class in its own file. No controller inheritance. No base classes.

---

## Middleware Pipeline (in order)

```
UseNovaProblemDetails          ← RFC 9457 error wrapper — must be first
UseCorrelationIdMiddleware     ← reads/generates X-Correlation-ID header
UseAuthentication              ← JWT validation
UseAuthorization
UseNovaRateLimiting            ← per-tenant fixed window (429 on breach)
UseTenantResolutionMiddleware  ← resolves TenantContext from JWT claim → DI
→ versioned route group /api/v{version:apiVersion}  ← business API endpoints (rate-limited)
→ unversioned routes                                 ← /test-db/*, /health/* (NOT rate-limited)
```

**Why this order matters:**
- `UseNovaProblemDetails` must wrap everything so all unhandled exceptions and 4xx/5xx responses are formatted as Problem Details.
- `CorrelationIdMiddleware` runs before auth so the correlation ID is available in error responses too.
- `UseNovaRateLimiting` runs after `UseAuthentication`/`UseAuthorization` so `HttpContext.User` is populated when the partition key (`tenant_id` claim) is evaluated.
- `TenantResolutionMiddleware` runs after auth because it reads the validated `tenant_id` JWT claim.
- Health checks and diagnostic endpoints are unversioned and registered outside the `v1` group — they are never rate-limited.

---

## RequestContext — Standard Request Base

Every POST and PATCH request record must inherit from `Nova.Shared.Requests.RequestContext`. This base record carries the seven fields that the frontend `apiClient` injects automatically on every request.

```csharp
// Nova.Shared/Requests/RequestContext.cs
public record RequestContext
{
    public string TenantId { get; init; } = string.Empty;       // wire: "tenant_id"
    public string CompanyId { get; init; } = string.Empty;      // wire: "company_id"
    public string BranchId { get; init; } = string.Empty;       // wire: "branch_id"
    public string UserId { get; init; } = string.Empty;         // wire: "user_id"
    public string BrowserLocale { get; init; } = string.Empty;  // wire: "browser_locale"
    public string BrowserTimezone { get; init; } = string.Empty;// wire: "browser_timezone"
    public string? IpAddress { get; init; }                     // wire: "ip_address" — optional
}
```

Domain request records inherit it and add their own fields:

```csharp
private sealed record SearchBookingsRequest : RequestContext
{
    public required DateOnly FromDate { get; init; }   // wire: "from_date"
    public required DateOnly ToDate { get; init; }     // wire: "to_date"
}
```

**Never** redeclare `TenantId`, `CompanyId`, etc. in the domain record — they come from the base.

---

## Validation Convention

Use `Nova.Shared.Validation.RequestContextValidator` in every POST/PATCH handler. Always follow this order:

```csharp
// 1. Validate standard context fields (400 if any required field is missing)
Dictionary<string, string[]> contextErrors = RequestContextValidator.Validate(request);
if (contextErrors.Count > 0)
    return TypedResults.ValidationProblem(contextErrors, title: "Validation failed");

// 2. Tenant mismatch check (403 if body tenant_id ≠ JWT-resolved tenant)
if (!RequestContextValidator.TenantMatches(request, tenantContext))
{
    return TypedResults.Problem(
        title: "Forbidden",
        detail: "tenant_id in the request body does not match the authenticated tenant.",
        statusCode: StatusCodes.Status403Forbidden);
}

// 3. Domain-specific field validation (400 for invalid input)
if (request.FromDate > request.ToDate)
{
    return TypedResults.ValidationProblem(
        errors: new Dictionary<string, string[]>
        {
            ["from_date"] = ["from_date must be before to_date."]
        },
        title: "Validation failed");
}

// 4. Business rule violations (422 — valid input but breaks a domain rule)
return TypedResults.Problem(
    title: "Booking cannot be cancelled",
    detail: "Bookings that have been invoiced cannot be cancelled.",
    statusCode: StatusCodes.Status422UnprocessableEntity);
```

**Order is mandatory.** Skipping step 1 means missing fields cause cryptic errors later. Skipping step 2 is a security gap. See `EchoEndpoint.cs` for the full annotated example.

---

## Adding a New Endpoint

### 1. Create the file

One endpoint = one static class = one file in `Endpoints/`.

```csharp
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;

namespace Nova.Shell.Api.Endpoints;

public static class BookingEndpoint
{
    // Map() accepts RouteGroupBuilder — the versioned group created in Program.cs.
    // Route is relative: /bookings/search becomes /api/v1/bookings/search at runtime.
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
        CancellationToken cancellationToken)          // always last — passed to all async DB calls
    {
        // Step 1 — standard context validation
        Dictionary<string, string[]> contextErrors = RequestContextValidator.Validate(request);
        if (contextErrors.Count > 0)
            return TypedResults.ValidationProblem(contextErrors, title: "Validation failed");

        // Step 2 — tenant mismatch
        if (!RequestContextValidator.TenantMatches(request, tenantContext))
            return TypedResults.Problem(title: "Forbidden", statusCode: StatusCodes.Status403Forbidden);

        // Step 3 — domain validation, then query, then return
        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);
        // ... await connection.QueryAsync<T>(sql, param, cancellationToken: cancellationToken);
    }

    private sealed record SearchBookingsRequest : RequestContext
    {
        public required DateOnly FromDate { get; init; }
        public required DateOnly ToDate { get; init; }
    }

    private sealed record BookingSummary(string Id, string Reference, DateOnly Date);
}
```

### 2. Register in Program.cs

```csharp
// Versioned API endpoints — registered on the v1 group (/api/v1/...)
HelloWorldEndpoint.Map(v1);
BookingEndpoint.Map(v1);      // ← add here
```

### 3. Rules

| Rule | Why |
|---|---|
| **Endpoint `Map()` accepts `RouteGroupBuilder`** | All business endpoints register on the versioned group (`v1`) created in `Program.cs`. Diagnostic endpoints (`TestDb*`) register on `app` directly. |
| **Use `RequireAuthorization()`** on any endpoint that injects `TenantContext` | `TenantContext` is set by `TenantResolutionMiddleware` only when a valid JWT is present. Without auth the DI factory throws → 500. |
| Request record inherits `RequestContext` | Ensures all 7 standard fields are always present |
| Use `RequestContextValidator.Validate()` first | Consistent validation before any business logic |
| Use `RequestContextValidator.TenantMatches()` second | Security — prevent cross-tenant access |
| Use `MapPost` for data retrieval | Keeps filters out of URLs, logs, and browser history |
| Use `MapPatch` for partial updates | Saves booking state, updates client details, etc. |
| Use `MapGet` only for parameter-free endpoints | `/health`, `/hello-world` |
| Use `TypedResults` not `Results` | Enables OpenAPI response metadata |
| Use `private sealed record` for request and response | Immutable, snake_case serialised automatically |
| Use `DateTimeOffset.UtcNow` not `DateTime.Now` | Timezone-safe across all environments |
| No string interpolation in SQL | Always Dapper parameterised queries |
| `CancellationToken cancellationToken` as last parameter on all `async` handlers | Allows the framework to cancel in-flight DB queries when the client disconnects |
| Pass `cancellationToken` to every Dapper async call | `QueryAsync`, `ExecuteAsync`, `ExecuteScalarAsync` all accept it — leaving it out wastes DB resources on abandoned requests |
| Always `using IDbConnection connection = ...` | Connections must be disposed promptly — `using` guarantees disposal even on exception |

---

## JSON Serialisation — snake_case

All request bodies and response bodies use **snake_case** on the wire. This is global — no attributes needed.

**C# record → wire format:**

```csharp
private sealed record SearchBookingsRequest(
    string TenantId,       // wire: "tenant_id"
    string BranchId,       // wire: "branch_id"
    DateOnly FromDate);    // wire: "from_date"
```

Incoming requests bind **case-insensitively** — `tenantId`, `tenant_id`, `TenantId` all bind to `TenantId`.

**Never add** `[JsonPropertyName]` to endpoint records — the global policy handles it.

---

## Error Handling — Problem Details (RFC 9457)

All error responses are `application/problem+json`. Every response includes `correlation_id` and `trace_id` extensions. No stack traces are ever exposed.

### Returning explicit errors from an endpoint

```csharp
// 400 — field-level validation failures
if (string.IsNullOrWhiteSpace(request.TenantId))
{
    return TypedResults.ValidationProblem(
        errors: new Dictionary<string, string[]>
        {
            ["tenant_id"] = ["tenant_id is required."]
        },
        title: "Validation failed");
}

// 403 — tenant mismatch (JWT tenant_id ≠ request body tenant_id)
if (request.TenantId != tenantContext.TenantId)
{
    return TypedResults.Problem(
        title: "Forbidden",
        detail: "tenant_id in request body does not match authenticated tenant.",
        statusCode: StatusCodes.Status403Forbidden);
}

// 404 — resource does not exist
if (booking is null)
{
    return TypedResults.Problem(
        title: "Booking not found",
        detail: $"No booking found with reference {request.Reference}.",
        statusCode: StatusCodes.Status404NotFound);
}

// 422 — business rule violation
return TypedResults.Problem(
    title: "Booking cannot be cancelled",
    detail: "Bookings that have been invoiced cannot be cancelled.",
    statusCode: StatusCodes.Status422UnprocessableEntity);
```

### Unhandled exceptions

Anything not caught in the handler falls through to `UseNovaProblemDetails`, which returns:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  "title": "An error occurred while processing your request.",
  "status": 500,
  "extensions": {
    "correlation_id": "...",
    "trace_id": "..."
  }
}
```

No stack trace. No exception message. Safe to expose publicly.

### Error response reference

| Situation | Return |
|---|---|
| Missing/invalid field | `TypedResults.ValidationProblem(errors, title)` |
| tenant_id mismatch | `TypedResults.Problem(..., 403)` |
| Resource not found | `TypedResults.Problem(..., 404)` |
| Business rule violation | `TypedResults.Problem(..., 422)` |
| Unhandled exception | Automatic 500 via `UseNovaProblemDetails` |

---

## Tenant-Aware Queries

The tenant's database connection is resolved from the JWT claim by `TenantResolutionMiddleware` and injected as `TenantContext` via DI.

```csharp
private static async Task<IResult> Handle(
    SearchBookingsRequest request,
    TenantContext tenantContext,           // injected — do not resolve manually
    IDbConnectionFactory connectionFactory,
    ISqlDialect dialect,                   // resolved from tenantContext.DbType
    CancellationToken cancellationToken)   // always last — passed to all async DB calls
{
    // Steps 1 & 2 — always first
    Dictionary<string, string[]> contextErrors = RequestContextValidator.Validate(request);
    if (contextErrors.Count > 0)
        return TypedResults.ValidationProblem(contextErrors, title: "Validation failed");

    if (!RequestContextValidator.TenantMatches(request, tenantContext))
        return TypedResults.Problem(title: "Forbidden", statusCode: StatusCodes.Status403Forbidden);

    // Query — connection is scoped to this tenant's database
    using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);
    string tableRef     = dialect.TableRef("bookings", "booking");
    string activeFilter = dialect.ActiveRowsFilter();   // "frz_ind = 0" or "frz_ind = false"
    string sql = $"SELECT id, reference FROM {tableRef} WHERE {activeFilter} AND branch_id = @BranchId";

    IEnumerable<BookingSummary> rows = await connection.QueryAsync<BookingSummary>(
        sql, new { BranchId = request.BranchId }, cancellationToken: cancellationToken);

    return TypedResults.Ok(rows);
}
```

**Dialect rules:**

| Method | MSSQL / MariaDB | Postgres |
|---|---|---|
| `TableRef("bookings", "booking")` | `bookings.dbo.booking` | `bookings.booking` |
| `BooleanLiteral(false)` | `0` | `false` |
| `ActiveRowsFilter()` | `frz_ind = 0` | `frz_ind = false` |
| `SoftDeleteClause()` | `frz_ind = 1` | `frz_ind = true` |
| `PaginationClause(skip, take)` | `ORDER BY (SELECT NULL) OFFSET … FETCH NEXT …` | `LIMIT … OFFSET …` |

- Never hardcode schema or table references as strings.
- Never hardcode `frz_ind = 0` or `frz_ind = 1` directly — use `ActiveRowsFilter()` / `SoftDeleteClause()`.
- `ISqlDialect` is injected as a scoped service by `AddNovaTenancy()` — the correct implementation is resolved automatically from `TenantContext.DbType`.

---

## Data Access — SQL Conventions

### No Entity Framework

**Entity Framework Core is not used and must not be added.** All data access is Dapper + explicit SQL.

Reasons:
- Multi-tenant per-DB architecture requires direct connection control — EF's `DbContext` lifetime model doesn't fit.
- MSSQL, Postgres, and MariaDB each need different SQL syntax; `ISqlDialect` handles that explicitly — EF's query translator does not.
- Explicit SQL is readable, reviewable, and exactly what runs against the database.

If you see `Microsoft.EntityFrameworkCore` in a `.csproj`, remove it.

---

### Package Stack

| Database | Driver package | Version |
|---|---|---|
| MSSQL | `Microsoft.Data.SqlClient` | `6.*` |
| Postgres | `Npgsql` | `9.*` |
| MariaDB / MySQL | `MySqlConnector` | `2.*` |
| All | `Dapper` | `2.*` |

These are declared in `Nova.Shared/Nova.Shared.csproj`. Domain services inherit them via the project reference — do not re-add them to domain service `.csproj` files.

Connection creation is handled by `IDbConnectionFactory` — never instantiate `SqlConnection`, `NpgsqlConnection`, or `MySqlConnection` directly in endpoints or handlers.

---

### Dapper Queries — Primary Pattern

Use `QueryAsync<T>` for result sets and `ExecuteScalarAsync<T>` for single values. Always use parameterised queries — never string interpolation for user-supplied values.

```csharp
using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

// SELECT — maps rows to a record automatically
string sql = $"""
    SELECT booking_ref, booking_date, total_amount
    FROM   {dialect.TableRef("bookings", "booking")}
    WHERE  {dialect.ActiveRowsFilter()}
    AND    branch_id = @BranchId
    AND    booking_date >= @FromDate
    ORDER  BY booking_date DESC
    {dialect.PaginationClause(request.Skip, request.PageSize)}
    """;

IEnumerable<BookingSummary> rows = await connection.QueryAsync<BookingSummary>(
    sql, new { request.BranchId, request.FromDate });

// COUNT
string countSql = $"""
    SELECT COUNT(*)
    FROM   {dialect.TableRef("bookings", "booking")}
    WHERE  {dialect.ActiveRowsFilter()}
    AND    branch_id = @BranchId
    """;

int totalCount = await connection.ExecuteScalarAsync<int>(countSql, new { request.BranchId });

// INSERT
string insertSql = $"""
    INSERT INTO {dialect.TableRef("bookings", "booking")} (branch_id, booking_date, created_by)
    VALUES (@BranchId, @BookingDate, @UserId)
    {dialect.ReturningIdClause()}
    """;

int newId = await connection.ExecuteScalarAsync<int>(insertSql,
    new { request.BranchId, BookingDate = DateOnly.FromDateTime(DateTime.UtcNow), UserId = request.UserId });

// SOFT DELETE — never hard-delete via the API
string deleteSql = $"""
    UPDATE {dialect.TableRef("bookings", "booking")}
    SET    {dialect.SoftDeleteClause()}, updated_by = @UserId, updated_at = @IpAddress
    WHERE  booking_ref = @BookingRef
    """;

await connection.ExecuteAsync(deleteSql,
    new { request.BookingRef, request.UserId, IpAddress = request.IpAddress ?? string.Empty });
```

**Rules:**
- Always use `@ParameterName` placeholders — Dapper binds the anonymous object properties by name.
- Use `dialect.TableRef()`, `dialect.ActiveRowsFilter()`, `dialect.SoftDeleteClause()`, `dialect.PaginationClause()` — never hardcode schema names, `frz_ind` values, or pagination syntax.
- Use raw string literals (`"""..."""`) for multi-line SQL — keeps indentation readable and avoids string concatenation.
- **SQL column aliases must match C# constructor parameter names** (case-insensitive, but no underscore stripping). Dapper maps by name — `crdate dep_date` will **not** bind to `DepDate`. Use `crdate DepDate` so the alias and the parameter match. This applies to any column whose DB name differs from the DTO property name.

---

### UTC Timestamps — Always Parameters, Never SQL Functions

Never call `GETDATE()`, `NOW()`, or `CURRENT_TIMESTAMP` in SQL. Always pass timestamps as Dapper parameters from the application.

```csharp
// Correct — application supplies the timestamp
string sql = $"""
    INSERT INTO {dialect.TableRef("bookings", "booking")}
        (booking_ref, branch_id, created_by, created_on)
    VALUES
        (@BookingRef, @BranchId, @UserId, @CreatedOn)
    {dialect.ReturningIdClause()}
    """;

await connection.ExecuteScalarAsync<int>(sql, new
{
    BookingRef = reference,
    request.BranchId,
    request.UserId,
    CreatedOn = DateTimeOffset.UtcNow        // application controls the clock
}, cancellationToken: cancellationToken);

// Wrong — never do this
// "INSERT INTO ... VALUES (@BookingRef, GETDATE())"    ← MSSQL only
// "INSERT INTO ... VALUES (@BookingRef, NOW())"        ← Postgres/MariaDB only
```

**Why:**
- `GETDATE()`/`NOW()`/`UTC_TIMESTAMP()` are different on every DB engine — adding a DB-level function call breaks cross-DB portability.
- When the timestamp comes from the application, tests can inject a fixed `DateTimeOffset` and assert the exact stored value. When the DB generates it, tests must query the row back and do approximate time comparisons.
- In migration replays, imports, and batch jobs you need to supply specific timestamps — the parameter approach supports this without a code change.

The same rule applies to `updated_on`: always set it explicitly in the `UPDATE` statement, never via a trigger.

---

### Transactions

Use an explicit `IDbTransaction` whenever a handler writes to more than one table, or writes a business record and an outbox message in the same operation. Never rely on implicit or hidden transaction behaviour.

```csharp
using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);
using IDbTransaction tx = connection.BeginTransaction();

try
{
    // Write the domain record
    await connection.ExecuteAsync(insertBookingSql, bookingParams,
        transaction: tx, cancellationToken: cancellationToken);

    // Write the outbox message in the same transaction
    await connection.ExecuteAsync(insertOutboxSql, outboxParams,
        transaction: tx, cancellationToken: cancellationToken);

    tx.Commit();
}
catch
{
    tx.Rollback();
    throw;
}
```

**Rules:**
- Always pass `transaction: tx` to every Dapper call inside the transaction — Dapper does not pick it up automatically.
- Commit explicitly — `tx.Commit()` — before the `using` block ends.
- Let the `catch` re-throw after rolling back — the global exception handler formats the 500 response.
- Keep transactions short: validate and prepare all data **before** opening the transaction, not inside it.
- A single-table write with no outbox does not need an explicit transaction — individual Dapper statements are auto-committed.

---

### Safe Reader Access

When Dapper's automatic mapping is insufficient (e.g. reading sparse columns, projecting a mix of nullable and non-nullable types), use `IDataReader` directly. Always use **ordinal-based access with null guards** — never cast from the indexer.

```csharp
using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);
using IDataReader reader = await connection.ExecuteReaderAsync(sql, parameters);

// Resolve ordinals once — before the read loop
int ordRef         = reader.GetOrdinal("booking_ref");
int ordDate        = reader.GetOrdinal("booking_date");   // DATE column → DateOnly
int ordCreatedOn   = reader.GetOrdinal("created_on");     // DATETIME2/TIMESTAMPTZ column → DateTimeOffset
int ordNotes       = reader.GetOrdinal("notes");          // nullable
int ordCancelledBy = reader.GetOrdinal("cancelled_by");   // nullable

var results = new List<BookingRow>();

while (await reader.ReadAsync())
{
    results.Add(new BookingRow(
        BookingRef:   reader.GetString(ordRef),
        BookingDate:  DateOnly.FromDateTime(reader.GetDateTimeSafe("booking_date")),  // DATE column
        CreatedOn:    reader.GetDateTimeOffsetSafe("created_on"),                     // UTC timestamp
        Notes:        reader.IsDBNull(ordNotes)       ? null : reader.GetString(ordNotes),
        CancelledBy:  reader.IsDBNull(ordCancelledBy) ? null : reader.GetString(ordCancelledBy)
    ));
}
```

**Rules:**
- Call `reader.GetOrdinal("column_name")` once before the loop — ordinal lookup is faster than name lookup on every row.
- For non-nullable columns: use the typed getter directly — `reader.GetString(ord)`, `reader.GetInt32(ord)`, `reader.GetDateTime(ord)`.
- For nullable columns: always guard with `reader.IsDBNull(ord) ? null : reader.GetXxx(ord)` — calling a typed getter on a `DBNull` column throws `InvalidCastException`.
- Never use `(string)reader["column_name"]` — name-based indexer returns `object`, the cast throws on `DBNull`, and name lookup repeats on every row.

---

### Identifiers — English (UK)

All C# identifiers — class names, method names, variable names, parameter names, property names — use **English (UK) spelling**.

| Prefer (UK) | Not (US) |
|---|---|
| `Initialise` | `Initialize` |
| `Colour` / `Colour` | `Color` |
| `Analyse` | `Analyze` |
| `Behaviour` | `Behavior` |
| `Authorise` | `Authorize` |
| `Recognise` | `Recognize` |
| `Serialise` | `Serialize` |
| `Cancelled` | `Canceled` |
| `Modelled` | `Modeled` |

This applies to identifiers only — not to third-party API method names you call (`JsonSerializer.Serialize(...)` is a framework method name, not ours).

---

## Logging Conventions

### Structured logging — never string interpolation

Always use message templates with named holes. Never use `$"..."` string interpolation in log calls.

```csharp
// Correct — structured, queryable in Datadog / OTel
_logger.LogInformation(
    "Booking {BookingRef} created for tenant {TenantId} by user {UserId}",
    booking.Reference, tenantContext.TenantId, request.UserId);

_logger.LogError(ex,
    "Failed to create booking for tenant {TenantId}",
    tenantContext.TenantId);

// Wrong — interpolated string loses structure
_logger.LogInformation($"Booking {booking.Reference} created for tenant {tenantContext.TenantId}");
```

With templates, log aggregation tools index `BookingRef`, `TenantId`, and `UserId` as searchable fields. With interpolation, the entire message is one opaque string.

### Log levels

| Level | Use for |
|---|---|
| `LogTrace` | Step-by-step diagnostic detail — never in production |
| `LogDebug` | Diagnostic information useful during development |
| `LogInformation` | Normal operation milestones (booking created, payment processed) |
| `LogWarning` | Unexpected but recoverable situations (retry attempt, tenant not found) |
| `LogError` | Failures that need attention — always include the exception |
| `LogCritical` | Application-level failures (startup failure, data corruption) |

Endpoints should log at `LogInformation` on success and `LogError` on exception. Do not log at `LogDebug` on every request — it fills the log under load.

### Never log sensitive data

The following must **never** appear in any log entry:

| Category | Examples |
|---|---|
| Credentials | Passwords, PINs, security question answers |
| Tokens | JWT access tokens, refresh tokens, API keys |
| Connection strings | Database URLs with username/password |
| Payment data | Card numbers, CVV, bank account numbers |
| Personal identifiable information (PII) | Passport numbers, national ID numbers, full card holder names in combination with other data |

This includes exception messages — a failed DB connection exception may contain the connection string. Log the exception type and a safe summary, not `ex.ToString()` verbatim when it may contain credentials.

```csharp
// Acceptable — logs the type and a safe message
_logger.LogError(ex, "Database connection failed for tenant {TenantId}", tenantId);

// Risky — ex.Message on a SqlException may contain the connection string
_logger.LogError("DB error: {Message}", ex.Message);
```

### Include tenant context in log entries

Every log entry in a handler or service that has resolved a `TenantContext` should include `TenantId`. This makes all logs filterable by tenant in Datadog:

```csharp
_logger.LogInformation(
    "Search returned {Count} bookings for tenant {TenantId}",
    rows.Count(), tenantContext.TenantId);
```

Correlation ID is added automatically to every log entry by the Serilog + OTel pipeline — you do not need to include it manually.

---

## When to Create an Interface

**Short rule:** create an interface only when multiple concrete implementations are plausible and will actually be built.

| Create an interface | Do not create an interface |
|---|---|
| Email: SendGrid vs Microsoft Graph | A booking repository with one implementation |
| File storage: local disk vs AWS S3 | A query class used in one place |
| Payment gateway: Stripe vs Adyen | An endpoint helper with no variant |
| SMS: Twilio vs Azure Communication | A utility class with static methods |
| `ISqlDialect` — three DB engines | A single logging wrapper |
| `IDbConnectionFactory` — three DB engines | A configuration helper |

`ISqlDialect` and `IDbConnectionFactory` are interfaces because there are three real implementations today. `TenantContext` is a record — no interface — because there is and always will be one implementation.

**Red flags that an interface is wrong:**
- The interface has exactly one method and one implementation.
- The interface name is just the class name with `I` prepended and no additional meaning.
- The interface exists "for testability" but the tests mock it with behaviour that doesn't reflect real usage.

For external service integrations (email, SMS, storage, payments), the interface lives in `Nova.Shared`. Implementations live in separate projects (`Nova.Shared.SendGrid`, `Nova.Shared.Stripe`). Each domain service explicitly chooses which implementation to register — the shared library does not force a choice.

---

## Testing Standards

### What to test

Every domain service endpoint requires at minimum:

| Test type | What it covers |
|---|---|
| Happy path | Valid request, correct data returned, correct status code |
| Validation failure | Missing required field returns 400 with correct `errors` key |
| Unauthorised | Missing JWT returns 401 |
| Tenant mismatch | Body `tenant_id` ≠ JWT tenant returns 403 |
| Not found | Resource does not exist returns 404 Problem Details |
| Soft-delete filter | Deleted records (`frz_ind = 1`) do not appear in results |

### Integration tests hit a real database

Do not mock `IDbConnectionFactory` or database connections. Tests that mock the DB validate nothing about SQL correctness, column names, or dialect differences. Use TestContainers or a shared local instance.

```csharp
// Use TestContainers to spin up a real DB per test run
await using var container = new MsSqlBuilder().Build();
await container.StartAsync();
string connectionString = container.GetConnectionString();
```

### Test all DB engines

Any query that uses `ISqlDialect` must be tested against MSSQL and Postgres at minimum. A query that passes on MSSQL and fails on Postgres is a production bug waiting for the first Postgres tenant to trigger it.

### Tenant isolation is a test requirement

Every integration test that reads data must verify that records from another tenant are not visible:

```csharp
// Insert data for tenant A and tenant B
// Query as tenant A
// Assert: only tenant A's records are returned, tenant B's are absent
```

This is not optional. Cross-tenant data leakage is a critical security failure.

### Tests are isolated and self-contained

- Each test creates its own data and tears it down — no shared state between tests.
- Tests do not depend on execution order.
- Tests do not depend on data seeded by another team member's local DB.

### Test project structure

```
src/
  services/
    Nova.Bookings.Api/
    Nova.Bookings.Api.Tests/
      Integration/
        BookingEndpointTests.cs     ← real DB, real HTTP
        TenantIsolationTests.cs     ← cross-tenant data visibility
      Unit/
        BookingValidationTests.cs   ← pure logic, no I/O
```

Domain service test projects are not in the shared library — they live next to their service.

---

## Standard Request Context Fields

These 7 fields come from `RequestContext` automatically when your request record inherits it. You do not redeclare them:

| Field | Wire key | Required | Usage |
|---|---|---|---|
| `TenantId` | `tenant_id` | Yes | Validate against JWT `TenantContext.TenantId` → 403 if mismatch |
| `CompanyId` | `company_id` | Yes | Scope queries to company |
| `BranchId` | `branch_id` | Yes | Scope queries to branch |
| `UserId` | `user_id` | Yes | Store in `created_by` / `updated_by` audit columns |
| `BrowserLocale` | `browser_locale` | No | Store for display preferences. Not used in business logic. |
| `BrowserTimezone` | `browser_timezone` | No | Store for display preferences. Not used in business logic. |
| `IpAddress` | `ip_address` | No | Store in `updated_at` audit column. Never use for security decisions — read `X-Forwarded-For` for that. |

`RequestContextValidator.Validate()` enforces the first four as required. The last three are informational only.

---

## Pagination Contract

All list and search endpoints use `PagedRequest` and `PagedResult<T>` from `Nova.Shared.Requests`.

### Request

```csharp
private sealed record SearchBookingsRequest : PagedRequest
{
    // PagedRequest already provides:
    //   page_number (default 1), page_size (default 25), Skip (computed)
    // Add domain-specific filter fields:
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
}
```

Wire format includes all 7 context fields plus:

| Field | Wire key | Default | Constraint |
|---|---|---|---|
| `PageNumber` | `page_number` | `1` | >= 1 |
| `PageSize` | `page_size` | `25` | 1–100 (enforced by `PagedRequestValidator`) |

### Validation order for paginated endpoints

```csharp
// Step 1 — context fields
var contextErrors = RequestContextValidator.Validate(request);
if (contextErrors.Count > 0)
    return TypedResults.ValidationProblem(contextErrors, title: "Validation failed");

// Step 2 — tenant mismatch
if (!RequestContextValidator.TenantMatches(request, tenantContext))
    return TypedResults.Problem(title: "Forbidden", statusCode: StatusCodes.Status403Forbidden);

// Step 3 — pagination fields
var pageErrors = PagedRequestValidator.Validate(request);
if (pageErrors.Count > 0)
    return TypedResults.ValidationProblem(pageErrors, title: "Validation failed");

// Step 4 — domain validation, then query
```

### SQL pattern

```csharp
string tableRef  = dialect.TableRef("bookings", "booking");
string pagination = dialect.PaginationClause(request.Skip, request.PageSize);

string active = dialect.ActiveRowsFilter();   // "frz_ind = 0" or "frz_ind = false"

// Two queries: one for count, one for data
string countSql = $"SELECT COUNT(*) FROM {tableRef} WHERE {active} AND branch_id = @BranchId";
string dataSql  = $"SELECT id, reference FROM {tableRef}"
                + $" WHERE {active} AND branch_id = @BranchId ORDER BY created_on DESC {pagination}";

using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);
int totalCount = await connection.ExecuteScalarAsync<int>(countSql, new { BranchId = request.BranchId });
IEnumerable<BookingSummary> rows = await connection.QueryAsync<BookingSummary>(dataSql, new { BranchId = request.BranchId });

return TypedResults.Ok(PagedResult<BookingSummary>.From(rows, totalCount, request.PageNumber, request.PageSize));
```

`request.Skip` = `(PageNumber - 1) * PageSize` — use it directly with `PaginationClause`.

`PaginationClause` generates:
- MSSQL: `ORDER BY (SELECT NULL) OFFSET {skip} ROWS FETCH NEXT {take} ROWS ONLY`
- Postgres / MariaDB: `LIMIT {take} OFFSET {skip}`

### Response shape

`PagedResult<T>.From(items, totalCount, pageNumber, pageSize)` produces (snake_case on the wire):

```json
{
  "items":             [...],
  "total_count":       47,
  "page_number":       2,
  "page_size":         10,
  "total_pages":       5,
  "has_next_page":     true,
  "has_previous_page": true
}
```

`total_pages`, `has_next_page`, and `has_previous_page` are computed — never store or pass them in.

---

## Outbound HTTP Clients — Resilience

All service-to-service and external HTTP calls use named `HttpClient` instances registered via `AddNovaHttpClient()` from `Nova.Shared.Web`.

### Registering a client (Program.cs, before `builder.Build()`)

```csharp
// 10. Outbound HTTP clients
builder.Services.AddNovaHttpClient("nova-auth",
    builder.Configuration["Services:NovaAuth:BaseUrl"] ?? "http://localhost:5200");
```

Add the corresponding entry to `appsettings.json`:

```json
"Services": {
    "NovaAuth": {
        "BaseUrl": "http://localhost:5200"
    }
}
```

### What every registered client gets

| Capability | Detail |
|---|---|
| `X-Correlation-ID` forwarding | The inbound request's correlation ID is automatically added to every outbound call — enables end-to-end tracing across services |
| Retry | Up to 3 retries with exponential back-off + jitter on `408`, `429`, and `5xx` responses |
| Circuit breaker | Opens after repeated failures; half-open after 30 s — prevents cascade failures |
| Per-attempt timeout | 10 s — fails fast if a single attempt hangs |
| Total request timeout | 30 s — caps the entire retry chain |

### Using the client in a handler

```csharp
private static async Task<IResult> Handle(IHttpClientFactory httpClientFactory)
{
    HttpClient client = httpClientFactory.CreateClient("nova-auth");

    // Resilience is transparent — call the target normally.
    // Retries and circuit breaker fire automatically on transient failures.
    HttpResponseMessage response = await client.GetAsync("/api/v1/users/me");

    if (!response.IsSuccessStatusCode)
        return TypedResults.Problem(
            title: "Auth service unavailable",
            statusCode: StatusCodes.Status502BadGateway);

    var body = await response.Content.ReadFromJsonAsync<UserProfile>();
    return TypedResults.Ok(body);
}
```

**Do not catch resilience exceptions** (`BrokenCircuitException`, `TimeoutRejectedException`) unless you need a specific non-500 response. Let them propagate to `UseNovaProblemDetails` — it returns a clean 500 with no stack trace.

See `HttpPingEndpoint.cs` for the fully annotated reference.

---

## Service-to-Service Authentication

When one Nova service calls another (e.g. `nova-shell` → `nova-auth`), the caller must prove it is a trusted Nova service, not an external client or user. This is done with a short-lived JWT that carries the **service name** as the subject — distinct from user JWTs which carry a user identity.

### Why separate from user JWT?

| Concern | User JWT | Internal JWT |
|---|---|---|
| Subject (`sub`) | User ID | Service name (`nova-shell`) |
| Audience (`aud`) | `nova-api` | `nova-internal` |
| Signing key | `Jwt.SecretKey` | `InternalAuth.SecretKey` |
| Issued by | Login endpoint | `ServiceTokenProvider` at runtime |
| Typical TTL | Minutes–hours | 5 minutes (300 s) |

Using a **separate key and audience** means a leaked internal token cannot be used to call user-facing endpoints, and vice versa.

### How it works — receiving side

`AddNovaInternalAuth()` registers a second JWT bearer scheme (`InternalJwt`) alongside the existing user scheme. The two schemes do not interfere — each validates a different audience.

```
appsettings.json → InternalAuth.SecretKey (encrypted)
                          ↓ decrypted at startup
InternalJwt scheme  →  validates: aud=nova-internal, iss=..., lifetime, signature
InternalService policy → .AddAuthenticationSchemes("InternalJwt").RequireAuthenticatedUser()
```

Endpoints that should only accept internal service calls are decorated with:

```csharp
.RequireAuthorization(InternalAuthConstants.PolicyName)   // "InternalService"
```

A request with a user JWT to such an endpoint gets **403 Forbidden** (wrong audience), not 401.

### How it works — calling side

`AddNovaInternalHttpClient()` registers a named `HttpClient` that attaches the internal Bearer token automatically via `ServiceTokenHandler`:

```
ServiceTokenProvider.GetTokenAsync()
    ├── fast path: return cached token (no crypto, no lock) if token expires in > 30 s
    └── slow path: generate new token under SemaphoreSlim, cache until (expiry - 30 s)
            ↓
    ServiceTokenHandler.SendAsync()  ←  DelegatingHandler on the internal HttpClient
            ↓
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
```

The token is generated once and reused until 30 seconds before expiry. A process handling thousands of internal calls per minute incurs one crypto operation every 270 seconds.

### Configuration (`appsettings.json`)

```json
"InternalAuth": {
  "ServiceName":          "nova-shell",
  "SecretKey":            "<encrypted-key>",
  "TokenLifetimeSeconds": 300
}
```

The same `SecretKey` must be deployed to **all** Nova services. All services share the same symmetric key for internal JWT validation.

### Wiring in Program.cs

```csharp
// Receiving side — validates incoming InternalJwt tokens
builder.AddNovaInternalAuth();

// Calling side — attaches token to outbound calls to internal services
builder.Services.AddNovaInternalHttpClient("nova-auth",
    builder.Configuration["Services:NovaAuth:BaseUrl"] ?? "http://localhost:5200");
```

### Protecting an endpoint

```csharp
app.MapGet("/internal/resource", Handle)
   .RequireAuthorization(InternalAuthConstants.PolicyName);
```

### Calling an internal endpoint

```csharp
private static async Task<IResult> Handle(IHttpClientFactory httpClientFactory)
{
    // Token is attached automatically by ServiceTokenHandler — no manual work.
    HttpClient client = httpClientFactory.CreateClient("nova-auth");
    HttpResponseMessage response = await client.GetAsync("/internal/resource");

    if (!response.IsSuccessStatusCode)
        return TypedResults.Problem(title: "Internal call failed",
            statusCode: StatusCodes.Status502BadGateway);

    var result = await response.Content.ReadFromJsonAsync<MyDto>();
    return TypedResults.Ok(result);
}
```

### Reading the caller identity on the receiving side

```csharp
private static IResult Handle(HttpContext context)
{
    // The sub claim is the calling service's ServiceName.
    string? callerService = context.User.FindFirst("sub")?.Value;
    // callerService == "nova-shell"
    return TypedResults.Ok(new { caller = callerService });
}
```

### Key files

| File | Purpose |
|---|---|
| `Nova.Shared/Auth/IServiceTokenProvider.cs` | Interface — inject this to get a token |
| `Nova.Shared.Web/Auth/ServiceTokenProvider.cs` | Singleton implementation with caching |
| `Nova.Shared.Web/Auth/InternalAuthConstants.cs` | Scheme/policy/audience string constants |
| `Nova.Shared.Web/Auth/InternalAuthExtensions.cs` | `AddNovaInternalAuth()` extension |
| `Nova.Shared.Web/Http/ServiceTokenHandler.cs` | DelegatingHandler that attaches the token |
| `Nova.Shared.Web/Http/HttpClientExtensions.cs` | `AddNovaInternalHttpClient()` extension |
| `Nova.Shell.Api/Endpoints/TestInternalAuthEndpoint.cs` | Dev diagnostic: get token, protected, call-self |

### Testing with the diagnostic endpoint

```
GET /test-internal-auth/token          → returns the raw JWT this service generates
GET /test-internal-auth/protected      → protected endpoint (needs InternalJwt token)
GET /test-internal-auth/call-self      → full round-trip: generate → attach → validate
```

See the inline comments in `TestInternalAuthEndpoint.cs` for a step-by-step manual testing guide including how to verify the token at jwt.io.

---

## Database Migrations (DbUp)

All schema changes are versioned SQL scripts managed by DbUp. Migrations run automatically at service startup — once per tenant, once per script, in alphabetical script order.

### Two-stage safety pipeline

Every pending script passes through two stages before executing.

#### Stage 1 — Absolute blocks (hard-coded, no override)

Two operations are **unconditionally prohibited** regardless of any config. They are blocked by `NeverAllowedDetector` before the policy check runs:

| Operation | Reason |
|---|---|
| `DROP DATABASE` | Destroys the entire tenant database |
| `DROP SCHEMA` | Destroys an entire schema and all objects in it |

These cannot be unlocked via any config file or flag.

#### Stage 2 — Per-engine allowlist (`migrationpolicy.json`)

All other commands are checked against the per-engine allowlist in `migrationpolicy.json`. If a command is **not in the list**, the script is blocked and logged as a structured warning — it does not execute. It will be re-evaluated on every startup until a DBA runs it manually and journals it.

`SqlCommandClassifier` extracts the canonical command name from each SQL statement (e.g. `"DROP TABLE"`, `"ALTER TABLE ADD"`, `"INSERT"`). `MigrationPolicyChecker` checks each against the allowlist. Utility statements (PRINT, SET, DECLARE, GO, USE) always pass — they return `null` from the classifier.

**Default allowlist (Nova.Shell.Api):**

| Command | MsSql | Postgres | MariaDb |
|---|:---:|:---:|:---:|
| `CREATE TABLE` | ✓ | ✓ | ✓ |
| `CREATE INDEX` | ✓ | ✓ | ✓ |
| `CREATE VIEW` | ✓ | ✓ | ✓ |
| `ALTER TABLE ADD` | ✓ | ✓ | ✓ |
| `ALTER TABLE SET` | ✓ | ✓ | |
| `INSERT` | ✓ | ✓ | ✓ |
| `UPDATE` | ✓ | ✓ | ✓ |
| `DELETE` | ✓ | ✓ | ✓ |
| `SELECT` | ✓ | ✓ | ✓ |

Commands **not** in the default list (e.g. `DROP TABLE`, `ALTER TABLE DROP`, `ALTER TABLE ALTER`, `TRUNCATE`) are blocked until explicitly added to the list.

To permit a command, add it to `migrationpolicy.json` and redeploy. The command name must match exactly the canonical name produced by `SqlCommandClassifier` (see the classifier's XML doc comment for the full list).

### Script layout

Scripts are embedded resources in each service project, organised by DB engine:

```
Nova.Shell.Api/
└── Migrations/
    ├── MsSql/
    │   ├── V001__CreateNovaOutbox.sql
    │   └── V002__AddSomething.sql
    ├── Postgres/
    │   ├── V001__CreateNovaOutbox.sql
    │   └── V002__AddSomething.sql
    └── MariaDb/
        ├── V001__CreateNovaOutbox.sql
        └── V002__AddSomething.sql
```

Naming convention: `V{NNN}__{Description}.sql` — scripts execute in alphabetical order, so the number prefix controls sequence. Use zero-padded three-digit numbers (`V001`, `V002`, ...).

The `.csproj` includes all `.sql` files as embedded resources:
```xml
<EmbeddedResource Include="Migrations\**\*.sql" />
```

### How it runs

At startup, before the app begins serving requests:

1. For each registered tenant, identify the matching script folder (based on `TenantRecord.DbType`)
2. Query DbUp's `SchemaVersions` journal table to find which scripts are already applied
3. For each pending script:
   - **Stage 1:** `NeverAllowedDetector` scans for `DROP DATABASE`, `DROP SCHEMA` → blocks immediately
   - **Stage 2:** `MigrationPolicyChecker` classifies every SQL statement via `SqlCommandClassifier` and checks against the engine's allowlist from `migrationpolicy.json`
4. If any stage blocks the script → entire script is skipped, structured warning logged
5. Scripts that pass both stages → handed to DbUp, run in their own transaction

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
MIGRATION BLOCKED  Tenant=BTDK  Script=Nova.Shell.Api.Migrations.MsSql.V003__RefactorOrders.sql
  2 issue(s) prevented automatic execution:
  • [NOT IN POLICY] Line 4: 'DROP TABLE' is not in the MsSql migration policy — DROP TABLE old_import_log
  • [NOT IN POLICY] Line 8: 'ALTER TABLE DROP' is not in the MsSql migration policy — ALTER TABLE orders DROP COLUMN legacy_ref
  ACTION REQUIRED: review and run the script manually on the database,
  then journal it so the runner stops flagging it:
  INSERT INTO SchemaVersions (ScriptName, Applied) VALUES ('...V003__RefactorOrders.sql', <now>);
  To allow the command to run automatically in future, add it to
  migrationpolicy.json → MigrationPolicy.MsSql
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

Blocked scripts log on **every startup** until resolved. This is intentional — persistent reminder until the DBA acts.

### Wiring in Program.cs

```csharp
// 16. Database migrations (before builder.Build())
builder.AddNovaMigrations();

// After app = builder.Build():
await app.RunNovaMigrationsAsync(typeof(Program).Assembly);
```

### Handling a blocked script

1. **Read the log** — identifies the script name, tenant, and exact lines blocked
2. **Review the script** — verify the operation is safe for each tenant's data
3. **Run manually** on the target database
4. **Journal it** so the runner stops flagging it:

```sql
-- MSSQL
INSERT INTO SchemaVersions (ScriptName, Applied)
VALUES ('Nova.Shell.Api.Migrations.MsSql.V003__DropOldColumn.sql', GETDATE());

-- Postgres
INSERT INTO schemaversions (scriptname, applied)
VALUES ('Nova.Shell.Api.Migrations.Postgres.V003__DropOldColumn.sql', NOW());

-- MariaDB / MySQL
INSERT INTO schemaversions (ScriptName, Applied)
VALUES ('Nova.Shell.Api.Migrations.MariaDb.V003__DropOldColumn.sql', NOW());
```

Next startup — script is in the journal → not pending → not logged.

### Writing safe migration scripts

Auto-executed by default (in the default allowlist):
```sql
-- Add a column
ALTER TABLE orders ADD COLUMN cancellation_reason NVARCHAR(500) NULL;

-- Create a new table
CREATE TABLE invoices ( ... );

-- Create an index
CREATE INDEX IX_orders_tenant ON orders (tenant_id, created_at);

-- Data backfill
UPDATE orders SET status = 'active' WHERE status IS NULL;
```

Blocked by default (not in the default allowlist — DBA must run manually and journal):
```sql
-- DROP TABLE — not in default list; add "DROP TABLE" to migrationpolicy.json to auto-run
DROP TABLE old_import_log;

-- ALTER TABLE DROP COLUMN — not in default list; add "ALTER TABLE DROP"
ALTER TABLE orders DROP COLUMN legacy_ref;

-- ALTER COLUMN — not in default list; add "ALTER TABLE ALTER" (MSSQL/Postgres)
-- or "ALTER TABLE MODIFY" (MariaDB) to auto-run
ALTER TABLE orders ALTER COLUMN notes VARCHAR(500);

-- TRUNCATE — not in default list
TRUNCATE TABLE orders;
```

Unconditionally blocked (no config can override):
```sql
-- Always blocked regardless of migrationpolicy.json
DROP DATABASE tenant_db;
DROP SCHEMA legacy_schema;
```

### Journal table (SchemaVersions)

DbUp automatically creates this table on first run. Do not rename or drop it.

| Column | Type | Notes |
|---|---|---|
| `ScriptName` | VARCHAR | The embedded resource name of the applied script |
| `Applied` | DATETIME | UTC timestamp of when the script ran |

The journal table exists per tenant database (each tenant has its own `SchemaVersions` table).

### Key files

| File | Purpose |
|---|---|
| `Nova.Shared/Migrations/IMigrationRunner.cs` | Interface — one method, one tenant |
| `Nova.Shared/Migrations/MigrationSummary.cs` | Result record — applied/blocked counts + `BlockedScript` list |
| `Nova.Shared.Web/Migrations/NeverAllowedDetector.cs` | Absolute blocks — `DROP DATABASE`, `DROP SCHEMA` (no config override) |
| `Nova.Shared.Web/Migrations/SqlCommandClassifier.cs` | Extracts canonical command name from a SQL statement |
| `Nova.Shared.Web/Migrations/MigrationPolicyChecker.cs` | Checks each classified command against the engine allowlist |
| `Nova.Shared.Web/Migrations/TenantMigrationRunner.cs` | Two-stage safety check then DbUp execution per tenant |
| `Nova.Shared.Web/Migrations/MigrationExtensions.cs` | `AddNovaMigrations()` + `RunNovaMigrationsAsync()` |
| `Nova.Shell.Api/migrationpolicy.json` | Per-engine allowlists (`MsSql`, `Postgres`, `MariaDb`) |
| `Nova.Shell.Api/Migrations/{DbType}/V001__*.sql` | Creates `nova_outbox` table |
| `Nova.Shell.Api/Migrations/{DbType}/V002__*.sql` | Adds relay columns (exchange, routing_key, status, …) |

---

## Outbox Relay

The outbox pattern guarantees at-least-once delivery of domain events. A handler writes the event to `nova_outbox` in the same DB transaction as its business write. A background worker (`OutboxRelayWorker`) polls `nova_outbox` and forwards messages to RabbitMQ or Redis Streams, depending on the tenant's `BrokerType`.

### Why outbox?

Without it, a handler that publishes directly to a broker can succeed in the DB but fail to publish (network blip, broker restart), silently losing the event. The outbox makes the two atomic — the event is only "sent" once it has been persisted.

### Broker selection (per tenant)

Each tenant has a `BrokerType` in `appsettings.json`:

```json
{
  "Tenants": [
    { "TenantId": "BTDK", "BrokerType": "RabbitMq", ... },
    { "TenantId": "client-b", "BrokerType": "Redis",    ... }
  ]
}
```

| `BrokerType` | How it publishes |
|---|---|
| `RabbitMq` | `BasicPublish` to `Exchange` with `RoutingKey`, persistent delivery mode |
| `Redis` | `XADD` to stream `nova:events:{Exchange}` — routing key stored as `event_type` field |

### Writing an outbox message

Write the outbox row in the **same transaction** as the business record:

```csharp
await connection.ExecuteAsync(insertBookingSql, bookingParams, transaction: tx);

await connection.ExecuteAsync("""
    INSERT INTO nova_outbox
        (aggregate_id, event_type, payload, exchange, routing_key,
         content_type, max_retries, status)
    VALUES
        (@AggregateId, @EventType, @Payload, @Exchange, @RoutingKey,
         'application/json', @MaxRetries, 'pending')
    """,
    new
    {
        AggregateId = booking.Id,
        EventType   = "booking.created",
        Payload     = JsonSerializer.Serialize(bookingEvent),
        Exchange    = "bookings",
        RoutingKey  = "booking.created",
        MaxRetries  = 5,
    },
    transaction: tx);

tx.Commit();
```

### How the relay works

`OutboxRelayWorker` is a `BackgroundService` that runs on every startup:

1. For each tenant, acquires a Redis distributed lock (`nova:outbox-relay:{tenantId}`) — ensures only one instance processes a tenant at a time.
2. Fetches up to `BatchSize` messages with `status = 'pending'` ordered by `created_at ASC`.
3. Marks them `processing`.
4. For each message: resolves the correct publisher and calls `PublishAsync`.
5. On success → marks `sent`, sets `processed_at`.
6. On failure → increments `retry_count`, stores `last_error`. If `retry_count >= max_retries` → marks `failed`. Otherwise resets to `pending` for the next cycle.

### Configuration

**`appsettings.json`** — RabbitMQ connection (static, per-deploy):
```json
{
  "RabbitMq": {
    "Host":        "localhost",
    "Port":        5672,
    "Username":    "guest",
    "Password":    "<encrypted-rabbitmq-password>",
    "VirtualHost": "/"
  }
}
```
The `Password` is encrypted via `ICipherService` (same pattern as JWT secret and tenant connection strings).

**`opsettings.json`** — relay tuning (hot-reloadable):
```json
{
  "OutboxRelay": {
    "Enabled":                true,
    "PollingIntervalSeconds": 5,
    "BatchSize":              50
  }
}
```
`Enabled: false` is an emergency stop — the worker skips all tenants until re-enabled, without a restart.

### Message status lifecycle

```
pending → processing → sent
                     ↘ pending  (retry_count < max_retries)
                     ↘ failed   (retry_count >= max_retries)
```

Failed messages require manual intervention — inspect `last_error`, fix the root cause, then reset `status = 'pending'` to requeue.

### Wiring in Program.cs

```csharp
// 17. Outbox relay (polls nova_outbox per tenant, publishes to RabbitMQ or Redis Streams)
builder.AddNovaOutboxRelay();
```

Call after `AddNovaDistributedLocking()` and `AddRedisClient()`.

### Key files

| File | Purpose |
|---|---|
| `Nova.Shared/Messaging/BrokerType.cs` | `RabbitMq` / `Redis` enum |
| `Nova.Shared/Messaging/Outbox/OutboxMessage.cs` | In-memory message record |
| `Nova.Shared/Messaging/Outbox/OutboxStatus.cs` | Status string constants |
| `Nova.Shared/Tenancy/TenantRecord.cs` | `BrokerType` property |
| `Nova.Shared/Configuration/AppSettings.cs` | `RabbitMqSettings` |
| `Nova.Shared/Configuration/OpsSettings.cs` | `OutboxRelaySettings` |
| `Nova.Shared.Web/Messaging/IMessagePublisher.cs` | Publish contract |
| `Nova.Shared.Web/Messaging/RabbitMqPublisher.cs` | RabbitMQ implementation |
| `Nova.Shared.Web/Messaging/RedisStreamPublisher.cs` | Redis Streams implementation |
| `Nova.Shared.Web/Messaging/OutboxRepository.cs` | Dapper queries — fetch/mark/retry |
| `Nova.Shared.Web/Messaging/OutboxRelayWorker.cs` | `BackgroundService` — main loop |
| `Nova.Shared.Web/Messaging/OutboxExtensions.cs` | `AddNovaOutboxRelay()` |

---

## Per-Tenant Rate Limiting

All business API endpoints (the versioned `/api/v1/...` group) are rate-limited automatically. Health checks and diagnostic endpoints are excluded.

### How it works

- **Policy:** fixed window, partitioned per request.
- **Partition key:** `tenant:{tenantId}` for authenticated requests; `ip:{remoteIp}` for anonymous.
- **On breach:** `429 Too Many Requests` with `application/problem+json` body and a `Retry-After` header.
- **Configured in:** `opsettings.json → RateLimiting` (hot-reloadable — changes take effect immediately without a restart).

### Configuration (`opsettings.json`)

```json
"RateLimiting": {
  "Enabled": true,
  "PermitLimit": 100,
  "WindowSeconds": 60,
  "QueueLimit": 0
}
```

| Field | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | `false` bypasses rate limiting entirely — for emergency disable without a deployment |
| `PermitLimit` | `100` | Maximum requests per tenant per window |
| `WindowSeconds` | `60` | Window duration in seconds |
| `QueueLimit` | `0` | Requests to queue when limit is reached. `0` = reject immediately (recommended) |

### 429 response format

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

Response headers include `Retry-After: {seconds}` when available.

### Applying the policy to a new service

The policy is applied to the entire versioned route group in `Program.cs` — no per-endpoint decoration needed:

```csharp
RouteGroupBuilder v1 = app.MapGroup("/api/v{version:apiVersion}")
    .WithApiVersionSet(versionSet)
    .MapToApiVersion(new ApiVersion(1, 0))
    .RequireRateLimiting(RateLimitingExtensions.PolicyName);   // all /api/v1/* endpoints
```

Health checks and diagnostic endpoints are registered on `app` directly and are never rate-limited.

### Disabling for a specific endpoint

If a particular endpoint must be exempt (e.g. a webhook receiver with its own auth):

```csharp
group.MapPost("/webhooks/receive", Handle)
     .DisableRateLimiting();
```

---

## Audit Columns

Every table must include these six columns after domain columns, in this order:

```sql
frz_ind     BIT / BOOLEAN       NOT NULL DEFAULT 0   -- soft-delete flag
created_on  DATETIME2 / TIMESTAMPTZ NOT NULL
created_by  NVARCHAR(10) / VARCHAR(10) NOT NULL
updated_on  DATETIME2 / TIMESTAMPTZ NULL
updated_by  NVARCHAR(10) / VARCHAR(10) NULL
updated_at  NVARCHAR(45) / VARCHAR(45) NULL           -- client IP address (not a datetime)
```

- `frz_ind = 1` = soft-deleted. All queries must filter using `dialect.ActiveRowsFilter()` — never hardcode `frz_ind = 0`.
- To soft-delete: `SET {dialect.SoftDeleteClause()} WHERE id = @id` — never hardcode `frz_ind = 1`.
- Never hard-delete via the API. Batch jobs hard-delete frozen records.
- Set `created_by` / `updated_by` from `request.UserId`. Set `updated_at` from `request.IpAddress`.

---

## Date and Time Handling

This is a travel application. Dates have two distinct semantics and the C# type chosen at the API boundary determines which rule applies. **Getting this wrong causes silent data corruption** — a check-in date shifted by a timezone offset produces an incorrect booking.

### UX contract (React and any frontend)

| Wire format | Meaning | UX behaviour |
|---|---|---|
| `"yyyy-MM-dd"` (no `T`, no offset) | Calendar date — fixed, never shifts | Displayed as-is; sent back as-is regardless of browser locale |
| `"yyyy-MM-ddTHH:mm:ssZ"` (with `T` and `Z`) | UTC timestamp — shifts for display | Displayed in browser local time; **UX must pre-shift back to UTC** before sending to API |

### How wire format is enforced (no SQL or attribute changes required)

The serialisation format is controlled entirely by the **C# type on the response/request record**, plus a **global JSON converter** registered in `AddNovaJsonOptions`:

| C# type | Wire format produced | How |
|---|---|---|
| `DateOnly` | `"yyyy-MM-dd"` | Automatic — STJ built-in, .NET 10 |
| `DateTimeOffset` | `"yyyy-MM-ddTHH:mm:ssZ"` | `UtcDateTimeOffsetConverter` — registered globally |

**Developer checklist:**
- Use `DateOnly` for calendar dates, `DateTimeOffset` for timestamps — the serialiser does the rest.
- Never use `DateTime` in request or response records.
- Never apply `[JsonConverter]` to individual date properties — the global converter handles all `DateTimeOffset` values uniformly.
- Never set the date format in SQL queries — format is a serialisation concern, not a DB concern.
- Ensure all `DateTimeOffset` values stored or returned are UTC. Use `DateTimeOffset.UtcNow` for server-generated values; call `.ToUniversalTime()` on client-supplied values before storing.

---

### Rule 1 — Calendar dates: `DateOnly` → wire format `yyyy-MM-dd`

Use `DateOnly` for any value that represents a calendar date with no time component:

- Booking date, check-in date, check-out date
- Travel date, departure date, return date
- Date of birth, document expiry date
- Any filter range: `from_date`, `to_date`

```csharp
// Request field
public required DateOnly CheckInDate { get; init; }   // wire: "check_in_date": "2026-06-15"
public required DateOnly CheckOutDate { get; init; }  // wire: "check_out_date": "2026-06-18"

// SQL parameter — stored as-is, no conversion
new { CheckInDate = request.CheckInDate, CheckOutDate = request.CheckOutDate }
```

**What the frontend sends:** `"2026-06-15"` — no time, no offset.  
**What the API receives:** `DateOnly(2026, 6, 15)` — no shifting, no timezone interpretation.  
**What is stored in the DB:** `2026-06-15` — exactly as supplied by the user.

`System.Text.Json` handles `DateOnly` natively in .NET 10 — no serialiser configuration needed. The value `"2026-06-15"` deserialises to `DateOnly` without any timezone influence regardless of where the browser is located.

**Never** use `DateTime` or `DateTimeOffset` for a calendar date. A `DateTime` carrying `2026-06-15T00:00:00` will be misread as a timestamp and may shift in timezone-aware contexts.

---

### Rule 2 — Timestamps: `DateTimeOffset` → wire format `yyyy-MM-ddThh:mm:ssZ`

Use `DateTimeOffset` for any value that represents a point in time:

- Audit timestamps: `created_on`, `updated_on`, `cancelled_on`
- Event times where the exact moment matters: flight departure time, session start
- Outbox message timestamps: `created_on`, `scheduled_on`, `processed_on`
- Any server-generated "now"

**Server-generated timestamps** — always use `DateTimeOffset.UtcNow`:

```csharp
new { CreatedOn = DateTimeOffset.UtcNow, UserId = request.UserId }
```

**Client-supplied timestamps** — the frontend sends the user's local time with their offset (e.g. `"2026-06-15T14:30:00+05:30"`). **Always normalise to UTC before storing:**

```csharp
// In the request record
public required DateTimeOffset DepartureTime { get; init; }

// Before the INSERT — normalise to UTC
DateTimeOffset departureUtc = request.DepartureTime.ToUniversalTime();
new { DepartureTime = departureUtc }
```

The `DATETIME2` / `TIMESTAMP` column in the DB stores UTC. `DATETIMEOFFSET` (MSSQL) and `TIMESTAMPTZ` (Postgres) normalise automatically — but calling `.ToUniversalTime()` explicitly makes the intent clear and works identically across all three engines.

**Never** use `DateTime` for a timestamp. `DateTime` does not carry timezone information. `DateTime.Now` is local time. `DateTime.UtcNow` is UTC but the type itself is ambiguous — `DateTime.Kind` is unenforceable at the API boundary. `DateTimeOffset` is always unambiguous.

---

### Rule 3 — `browser_timezone` and display conversion

`browser_timezone` (e.g. `"Europe/London"`, `"Asia/Kolkata"`) is sent by the frontend on every request via `RequestContext`. Its purpose in a travel application is:

- **Do not** use it to interpret incoming dates or times. The frontend is responsible for converting user input to the correct wire format before sending.
- **Do** store it alongside search/booking records so the UI can reconstruct local-time display for that session.
- **Do** use it when sending time-based notifications: convert a UTC timestamp back to the user's timezone for the notification body.

```csharp
// Storing for display — save with the booking record
new { BrowserTimezone = request.BrowserTimezone }

// Converting for display in a notification (application layer, not API layer)
TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(booking.BrowserTimezone);
DateTimeOffset localDeparture = TimeZoneInfo.ConvertTime(departureUtc, tz);
```

**Never** use `browser_timezone` to adjust a `DateOnly` field. A check-in date of `2026-06-15` is `2026-06-15` in every timezone.

---

### Rule 3b — DATETIME2 → DateTimeOffset (MSSQL-specific pattern)

`DATETIME2` is the standard MSSQL column type for UTC timestamps. It has **no timezone information** — ADO.NET returns it as a `DateTime` with `Kind = Unspecified`.

**Never** map a `DATETIME2` column directly to a `DateTimeOffset` property in a Dapper DTO. Dapper calls `new DateTimeOffset(dateTime)` which applies the **local server timezone offset** — silently wrong on any server not running UTC.

The correct two-step pattern:

```csharp
// Step 1 — Dapper DTO: map DATETIME2 to DateTime
private sealed record BookingRow(
    string   BookingRef,
    DateOnly BookingDate,   // DATE column     → DateOnly (Dapper 2.x maps this directly)
    DateTime CreatedOn,     // DATETIME2 column → DateTime, Kind = Unspecified
    DateTime UpdatedOn);    // DATETIME2 column → DateTime, Kind = Unspecified

// Step 2 — Response type: convert DateTime → DateTimeOffset UTC
private sealed record BookingResponse(
    string         BookingRef,
    DateOnly       BookingDate,  // wire: "2026-08-15"
    DateTimeOffset CreatedOn,    // wire: "2026-04-03T10:00:00Z"
    DateTimeOffset UpdatedOn);   // wire: "2026-04-03T10:00:00Z"

// Step 3 — Projection in handler
IEnumerable<BookingResponse> response = rows.Select(r => new BookingResponse(
    BookingRef:  r.BookingRef,
    BookingDate: r.BookingDate,
    CreatedOn:   new DateTimeOffset(DateTime.SpecifyKind(r.CreatedOn, DateTimeKind.Utc)),
    UpdatedOn:   new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedOn, DateTimeKind.Utc))));
```

`DateTime.SpecifyKind(dt, DateTimeKind.Utc)` does not convert the value — it stamps the `Kind` flag. `new DateTimeOffset(utcDateTime)` then wraps it with zero offset. The global `UtcDateTimeOffsetConverter` serialises it to the wire as `Z` (not `+00:00`).

**Alternative:** use `DATETIMEOFFSET` column type in MSSQL if you need the offset stored in the DB. Dapper maps `DATETIMEOFFSET` → `DateTimeOffset` directly, no conversion needed. For Postgres, `TIMESTAMPTZ` always stores UTC and maps to `DateTimeOffset` cleanly.

---

### Rule 4 — Reading timestamps from the DB (`IDataReader`)

Use the correct `SafeReaderExtensions` method:

| Column type | C# target | Method to use |
|---|---|---|
| Date-only (`DATE`, `date`) | `DateOnly` | `GetDateTimeSafe` then `DateOnly.FromDateTime(...)` |
| UTC timestamp (`DATETIME2`, `TIMESTAMPTZ`) | `DateTimeOffset` | `GetDateTimeOffsetSafe` |

```csharp
// Reading a date-only column
DateOnly bookingDate = DateOnly.FromDateTime(reader.GetDateTimeSafe("booking_date"));

// Reading a UTC timestamp column
DateTimeOffset createdOn = reader.GetDateTimeOffsetSafe("created_on");
```

Do **not** use `GetDateTimeSafe` for UTC timestamp columns — it returns a `DateTime` with no timezone information.

---

### End-to-end example — booking record with both date types

A hotel booking has two calendar dates (never shift) and two audit timestamps (shift for display).

The **request record**, **INSERT**, **wire response**, and **UX treatment** are identical for all three engines.
Only the **DB schema** and **Dapper DTO / projection** differ — each engine returns timestamps differently from ADO.NET.

---

#### MSSQL

##### DB schema

```sql
CREATE TABLE bookings.booking (
    booking_ref   VARCHAR(20)  NOT NULL,
    check_in      DATE         NOT NULL,   -- calendar date — no time
    check_out     DATE         NOT NULL,   -- calendar date — no time
    created_on    DATETIME2    NOT NULL,   -- UTC timestamp — Kind = Unspecified from ADO.NET
    updated_on    DATETIME2    NOT NULL    -- UTC timestamp — Kind = Unspecified from ADO.NET
);
```

##### Request record (what UX sends)

```csharp
public sealed record CreateBookingRequest
{
    public required string   BookingRef  { get; init; }
    public required DateOnly CheckIn     { get; init; }  // wire: "2026-08-15"  — never shifts
    public required DateOnly CheckOut    { get; init; }  // wire: "2026-08-20"  — never shifts
}
```

UX sends:
```json
{
  "booking_ref": "BK-001",
  "check_in":    "2026-08-15",
  "check_out":   "2026-08-20"
}
```

`check_in` and `check_out` have no `T` or `Z` — the API binds them as `DateOnly` with no timezone influence.

##### INSERT handler

```csharp
string sql = $"""
    INSERT INTO {dialect.TableRef("bookings", "booking")}
        (booking_ref, check_in, check_out, created_on, updated_on)
    VALUES
        (@BookingRef, @CheckIn, @CheckOut, @CreatedOn, @UpdatedOn)
    """;

await connection.ExecuteAsync(sql, new
{
    request.BookingRef,
    request.CheckIn,                  // DateOnly   → stored as DATE
    request.CheckOut,                 // DateOnly   → stored as DATE
    CreatedOn = DateTimeOffset.UtcNow, // DateTimeOffset UTC → stored as DATETIME2
    UpdatedOn = DateTimeOffset.UtcNow,
}, cancellationToken: ct);
```

No format strings, no `.ToString()` calls — Dapper passes the values as typed parameters.

##### SELECT — Dapper DTO (raw) and response record (wire)

Because `DATETIME2` returns `DateTime(Kind=Unspecified)` from ADO.NET, the two-record pattern is required:

```csharp
// Dapper DTO — mirrors exactly what ADO.NET returns
private sealed record BookingRow(
    string   BookingRef,
    DateOnly CheckIn,     // DATE column    → Dapper 2.x maps DATE → DateOnly directly
    DateOnly CheckOut,    // DATE column    → Dapper 2.x maps DATE → DateOnly directly
    DateTime CreatedOn,   // DATETIME2 column → DateTime, Kind = Unspecified
    DateTime UpdatedOn);  // DATETIME2 column → DateTime, Kind = Unspecified

// Response record — what the API sends to UX
private sealed record BookingResponse(
    string         BookingRef,
    DateOnly       CheckIn,    // wire: "check_in":    "2026-08-15"     ← never shifts
    DateOnly       CheckOut,   // wire: "check_out":   "2026-08-20"     ← never shifts
    DateTimeOffset CreatedOn,  // wire: "created_on":  "2026-04-03T09:00:00Z"  ← shifts for display
    DateTimeOffset UpdatedOn); // wire: "updated_on":  "2026-04-03T11:30:00Z"  ← shifts for display
```

##### Projection — DTO → response

```csharp
string sql = $"""
    SELECT booking_ref BookingRef,
           check_in    CheckIn,
           check_out   CheckOut,
           created_on  CreatedOn,
           updated_on  UpdatedOn
    FROM   {dialect.TableRef("bookings", "booking")}
    WHERE  booking_ref = @BookingRef
    """;

BookingRow? row = await connection.QuerySingleOrDefaultAsync<BookingRow>(
    sql, new { request.BookingRef }, cancellationToken: ct);

// Project: DateOnly passes through; DateTime → DateTimeOffset UTC
BookingResponse response = new(
    BookingRef: row.BookingRef,
    CheckIn:    row.CheckIn,
    CheckOut:   row.CheckOut,
    CreatedOn:  new DateTimeOffset(DateTime.SpecifyKind(row.CreatedOn, DateTimeKind.Utc)),
    UpdatedOn:  new DateTimeOffset(DateTime.SpecifyKind(row.UpdatedOn, DateTimeKind.Utc)));
```

##### Wire response (what UX receives) — identical for all engines

```json
{
  "booking_ref": "BK-001",
  "check_in":    "2026-08-15",
  "check_out":   "2026-08-20",
  "created_on":  "2026-04-03T09:00:00Z",
  "updated_on":  "2026-04-03T11:30:00Z"
}
```

##### What UX does with each field — identical for all engines

| Field | UX treatment |
|---|---|
| `check_in`, `check_out` | Displayed as-is (`"2026-08-15"` → `15 Aug 2026`). Never fed to `new Date(...)`. Never shifted. |
| `created_on`, `updated_on` | Fed to `new Date("2026-04-03T09:00:00Z")`. Browser converts to local time for display (e.g. `3 Apr 2026, 14:30` in IST). |

When UX sends `created_on` or `updated_on` back to the API it pre-shifts to UTC first — `dateObj.toISOString()` in JavaScript produces `"...Z"` automatically.

---

#### Postgres

Postgres is the cleanest engine for dates. `TIMESTAMPTZ` is always stored and returned as UTC — Npgsql maps it directly to `DateTimeOffset` with no conversion. **No two-record pattern is required.**

##### DB schema

```sql
CREATE TABLE bookings.booking (
    booking_ref   VARCHAR(20)  NOT NULL,
    check_in      date         NOT NULL,        -- calendar date — Npgsql → DateOnly directly
    check_out     date         NOT NULL,        -- calendar date — Npgsql → DateOnly directly
    created_on    timestamptz  NOT NULL,        -- UTC timestamp — Npgsql → DateTimeOffset directly
    updated_on    timestamptz  NOT NULL         -- UTC timestamp — Npgsql → DateTimeOffset directly
);
```

##### SELECT — Dapper DTO (no projection needed)

Because Npgsql maps `date` → `DateOnly` and `timestamptz` → `DateTimeOffset` natively, the Dapper DTO **is** the response record:

```csharp
// Single record — no conversion step needed
private sealed record BookingResponse(
    string         BookingRef,
    DateOnly       CheckIn,    // date        → DateOnly         — wire: "2026-08-15"
    DateOnly       CheckOut,   // date        → DateOnly         — wire: "2026-08-20"
    DateTimeOffset CreatedOn,  // timestamptz → DateTimeOffset   — wire: "2026-04-03T09:00:00Z"
    DateTimeOffset UpdatedOn); // timestamptz → DateTimeOffset   — wire: "2026-04-03T11:30:00Z"

// Query — aliases match C# parameter names (Dapper rule)
string sql = $"""
    SELECT booking_ref BookingRef,
           check_in    CheckIn,
           check_out   CheckOut,
           created_on  CreatedOn,
           updated_on  UpdatedOn
    FROM   {dialect.TableRef("bookings", "booking")}
    WHERE  booking_ref = @BookingRef
    """;

BookingResponse? response = await connection.QuerySingleOrDefaultAsync<BookingResponse>(
    sql, new { request.BookingRef }, cancellationToken: ct);
```

No `DateTime.SpecifyKind`, no `new DateTimeOffset(...)` — Npgsql handles the UTC normalisation internally.

> **Use `timestamptz`, never `timestamp`.**  
> `timestamp` (without time zone) returns `DateTime(Kind=Unspecified)` from Npgsql — the same trap as MSSQL `DATETIME2`, requiring the two-record pattern. Always use `timestamptz` for new Postgres schemas.

---

#### MariaDB

MariaDB has no native `DATETIMEOFFSET` equivalent. The `DATETIME` column returns `DateTime(Kind=Unspecified)` from MySqlConnector — the **same two-record pattern as MSSQL is required**.

##### DB schema

```sql
CREATE TABLE bookings.booking (
    booking_ref   VARCHAR(20)  NOT NULL,
    check_in      DATE         NOT NULL,    -- calendar date — MySqlConnector → DateOnly directly
    check_out     DATE         NOT NULL,    -- calendar date — MySqlConnector → DateOnly directly
    created_on    DATETIME     NOT NULL,    -- UTC stored by convention — returns DateTime(Kind=Unspecified)
    updated_on    DATETIME     NOT NULL     -- UTC stored by convention — returns DateTime(Kind=Unspecified)
);
```

> MariaDB `DATETIME` has no timezone awareness — UTC is stored by application convention, not enforced by the DB. Always pass `DateTimeOffset.UtcNow` as the parameter when inserting; never rely on `NOW()` or `CURRENT_TIMESTAMP` in SQL.

##### SELECT — Dapper DTO (raw) and projection

The pattern is identical to MSSQL:

```csharp
// Dapper DTO — mirrors what MySqlConnector returns
private sealed record BookingRow(
    string   BookingRef,
    DateOnly CheckIn,     // DATE     → DateOnly directly (MySqlConnector 2.x)
    DateOnly CheckOut,    // DATE     → DateOnly directly (MySqlConnector 2.x)
    DateTime CreatedOn,   // DATETIME → DateTime, Kind = Unspecified
    DateTime UpdatedOn);  // DATETIME → DateTime, Kind = Unspecified

// Response record — wire format
private sealed record BookingResponse(
    string         BookingRef,
    DateOnly       CheckIn,    // wire: "2026-08-15"
    DateOnly       CheckOut,   // wire: "2026-08-20"
    DateTimeOffset CreatedOn,  // wire: "2026-04-03T09:00:00Z"
    DateTimeOffset UpdatedOn); // wire: "2026-04-03T11:30:00Z"

// Projection — identical to MSSQL
BookingResponse response = new(
    BookingRef: row.BookingRef,
    CheckIn:    row.CheckIn,
    CheckOut:   row.CheckOut,
    CreatedOn:  new DateTimeOffset(DateTime.SpecifyKind(row.CreatedOn, DateTimeKind.Utc)),
    UpdatedOn:  new DateTimeOffset(DateTime.SpecifyKind(row.UpdatedOn, DateTimeKind.Utc)));
```

---

#### Engine comparison at a glance

| | MSSQL | Postgres | MariaDB |
|---|---|---|---|
| Calendar date column | `DATE` | `date` | `DATE` |
| `DATE` → C# via ADO.NET | `DateTime` (legacy) or `DateOnly` (MSSQL 2019+) | `DateOnly` directly | `DateOnly` directly |
| Timestamp column | `DATETIME2` | `timestamptz` | `DATETIME` |
| Timestamp → C# via ADO.NET | `DateTime(Kind=Unspecified)` | `DateTimeOffset` UTC directly | `DateTime(Kind=Unspecified)` |
| Two-record pattern needed? | Yes — `DATETIME2` needs `SpecifyKind` | **No** — `timestamptz` maps cleanly | Yes — `DATETIME` needs `SpecifyKind` |
| `TableRef` format | `[schema].[table]` | `"schema"."table"` | `` `schema`.`table` `` |

> **MSSQL note on `DATE` columns:** Dapper on MSSQL 2019+ can map `DATE` → `DateOnly` directly. On older MSSQL versions, `DATE` returns `DateTime` and requires `DateOnly.FromDateTime(dt)` in the projection. If in doubt, use the two-record pattern for all MSSQL date columns.

---

### Summary

| Scenario | C# type | Wire format | MSSQL | Postgres | MariaDB | Rule |
|---|---|---|---|---|---|---|
| Check-in / check-out date | `DateOnly` | `"2026-06-15"` | `DATE` | `date` | `DATE` | Never shift |
| Booking / travel date | `DateOnly` | `"2026-06-15"` | `DATE` | `date` | `DATE` | Never shift |
| Date range filter | `DateOnly` | `"2026-06-15"` | — param | — param | — param | Never shift |
| Server audit timestamp | `DateTimeOffset` | `"2026-06-15T10:00:00Z"` | `DATETIME2` | `timestamptz` | `DATETIME` | `DateTimeOffset.UtcNow` |
| Client-supplied event time | `DateTimeOffset` | `"2026-06-15T14:30:00Z"` (UX pre-shifts) | `DATETIME2` | `timestamptz` | `DATETIME` | `.ToUniversalTime()` before storing |
| Outbox message timestamp | `DateTimeOffset` | — (internal) | `DATETIME2` | `timestamptz` | `DATETIME` | `DateTimeOffset.UtcNow` |
| **Two-record pattern needed?** | | | **Yes** | **No** | **Yes** | `timestamptz` maps cleanly; `DATETIME`/`DATETIME2` need `SpecifyKind` |

---

## Distributed Locking

> **New to distributed locking?** Read `docs/distributed-locking-guide.md` first.
> It covers the problem from first principles, the unique token, the Lua script, `await using`,
> common mistakes, debugging with `redis-cli`, and a pre-merge checklist.
> This section is the quick reference — the guide is the learning resource.

### What it is and why it is needed

In a single-instance app, `lock(obj)` prevents two threads running the same code simultaneously. In a multi-instance deployment (multiple containers), `lock` has no effect across processes — each process has its own memory.

A **distributed lock** uses Redis as the coordination point. All instances check Redis before entering a critical section. Only the instance that acquires the lock proceeds; the others get `null` back and must return a `Conflict` response or retry.

**Without a distributed lock** — double-booking race condition:

```
Instance A: reads seat BK-001 — available
Instance B: reads seat BK-001 — available       ← race: both pass the check
Instance A: inserts booking for BK-001  ✓
Instance B: inserts booking for BK-001  ✓        ← double booking
```

**With a distributed lock**:

```
Instance A: SET nova:booking:BK-001 tokenA NX  → OK     (lock acquired)
Instance B: SET nova:booking:BK-001 tokenB NX  → nil    (lock already held)
Instance B: returns 409 Conflict ─────────────────────────────────────────
Instance A: checks availability, inserts booking, releases lock
```

---

### How it works in Redis (SET NX)

Acquire — one atomic command:

```
SET resource_key unique_token NX PX ttl_milliseconds
  NX  = Only set if Not eXists (atomic check-and-set)
  PX  = expiry in milliseconds (the TTL)
→ OK   = lock acquired — key did not exist, we created it
→ nil  = lock not acquired — key already exists (held by another instance)
```

Release — Lua script (atomic check-then-delete):

```lua
if redis.call("GET", KEYS[1]) == ARGV[1] then
    return redis.call("DEL", KEYS[1])
else
    return 0   -- key expired or belongs to another instance — do nothing
end
```

The unique token (a UUID per acquisition) ensures only the holder can delete the key. Without it, an instance whose TTL expired could delete a lock held by a different instance.

**Why Lua for release?** — The check and delete must be atomic. If done as two separate Redis commands, the TTL could expire between them and another instance could acquire the lock. The first instance would then delete the second instance's lock. Redis executes Lua scripts as a single indivisible unit — no other client command runs between the GET and the DEL.

---

### The TTL is the safety net

Every lock must have a TTL. If a process crashes while holding a lock, the TTL ensures Redis removes the key automatically. Without a TTL, the resource would be locked forever.

| Operation | Recommended TTL |
|---|---|
| DB read + write (booking create) | 15–30 s |
| Payment gateway call | 30–60 s |
| Background job | Expected duration + 20 % buffer |
| Cache rebuild (stampede prevention) | 5–10 s |

If the operation exceeds the TTL, the lock expires and another instance could enter. Design writes to be **idempotent** and always back locks with a **DB unique constraint** as the final safety net.

---

### When to use distributed locking

**Use it for:**
- Preventing double-booking — two users booking the same seat/room simultaneously
- Payment idempotency — ensuring a payment reaches the gateway exactly once
- Inventory reservation — check-then-reserve must be atomic across instances
- Background jobs — ensuring a scheduled task (send reminders, process outbox) runs on one instance

**Do not use it for:**
- Read-only operations — reads do not mutate state, no lock needed
- Replacing a DB unique constraint — use both; the lock prevents the race, the DB constraint is the last line of defence
- Long-running operations (> 60 s) — the TTL may expire before the work finishes

---

### Interface

```csharp
public interface IDistributedLockService
{
    // Returns a held lock, or null if the resource is already locked or Redis is unavailable.
    Task<IDistributedLock?> TryAcquireAsync(
        string resource, TimeSpan expiry, CancellationToken ct = default);
}

public interface IDistributedLock : IAsyncDisposable
{
    string Resource { get; }   // the Redis key that was locked
    // DisposeAsync() releases the lock via Lua script
}
```

---

### Key naming convention

| Scenario | Format | Example |
|---|---|---|
| Tenant-scoped write | `tenant:{tenantId}:{entity}:{id}` | `tenant:BTDK:booking:create:BK-001` |
| Tenant-scoped payment | `tenant:{tenantId}:payment:{ref}` | `tenant:BTDK:payment:PAY-999` |
| Global background job | `nova:job:{job-name}` | `nova:job:send-reminders` |
| Cache stampede prevention | `lock:{cacheKey}` | `lock:tenant:BTDK:config:v1` |

---

### Usage patterns

#### Pattern 1 — Prevent double booking (most common)

```csharp
string lockKey = $"tenant:{tenantId}:booking:create:{request.BookingRef}";

await using IDistributedLock? lk = await _lockService.TryAcquireAsync(
    resource: lockKey,
    expiry:   TimeSpan.FromSeconds(30),
    ct:       ct);

if (lk is null)
    return Results.Conflict("This booking is already being processed. Try again shortly.");

// Critical section — only one instance reaches here at a time
bool alreadyExists = await _repo.BookingExistsAsync(request.BookingRef, ct);
if (alreadyExists)
    return Results.Conflict("Booking reference already exists.");

await _repo.CreateBookingAsync(request, ct);
// await using releases the lock automatically here
```

#### Pattern 2 — Payment idempotency

```csharp
string lockKey = $"tenant:{tenantId}:payment:{request.PaymentRef}";

await using IDistributedLock? lk = await _lockService.TryAcquireAsync(
    resource: lockKey,
    expiry:   TimeSpan.FromSeconds(60),
    ct:       ct);

if (lk is null)
    return Results.Conflict("Payment is already being processed.");

// Only one instance submits this payment reference to the gateway
PaymentResult result = await _gateway.SubmitAsync(request, ct);
```

#### Pattern 3 — Background job (one instance only)

```csharp
// In a hosted service or scheduled job runner
await using IDistributedLock? lk = await _lockService.TryAcquireAsync(
    resource: "nova:job:send-reminders",
    expiry:   TimeSpan.FromMinutes(10),
    ct:       ct);

if (lk is null)
{
    _logger.LogInformation("Send-reminders job already running on another instance — skipping.");
    return;
}

await _reminderService.SendPendingAsync(ct);
```

#### Pattern 4 — Cache stampede prevention

When many requests arrive simultaneously after a cache miss, only one should rebuild the cache. Without a lock, all instances would hit the DB at the same time.

```csharp
TenantConfig? config = await _cache.GetOrSetAsync<TenantConfig>(
    key:         $"tenant:{tenantId}:config:v1",
    profileName: "ReferenceData",
    factory:     async () =>
    {
        string lockKey = $"lock:tenant:{tenantId}:config:v1";

        await using IDistributedLock? lk = await _lockService.TryAcquireAsync(
            lockKey, TimeSpan.FromSeconds(10), ct);

        if (lk is null)
        {
            // Another instance is rebuilding — wait briefly then read from cache
            await Task.Delay(200, ct);
            return await _repo.GetTenantConfigAsync(ct); // or re-try cache
        }

        return await _repo.GetTenantConfigAsync(ct);
    },
    ct: ct);
```

---

### Rules for the dev team

1. **Always `await using`** — never manually call `DisposeAsync()`. The `await using` block guarantees release even on exception.
2. **Always check for `null`** — `TryAcquireAsync` returns `null` when the lock is held or Redis is unavailable. Proceeding without a lock defeats the purpose.
3. **Always choose a TTL longer than the expected operation** — add a generous buffer. A lock that expires too early is worse than no lock (it gives a false sense of safety).
4. **Always pair with a DB unique constraint** — the lock prevents the race; the constraint catches any edge case where the lock TTL expired mid-operation.
5. **Never hold a lock across a `Task.Delay` in production** — holding a lock while waiting wastes the TTL. Only use delays in test/diagnostic endpoints.
6. **Keep critical sections short** — acquire the lock, do the work, release immediately. Do not hold a lock while doing unrelated work.
7. **Do not nest locks** — acquiring lock B while holding lock A risks deadlock if another instance does it in reverse order.

---

### Registration

```csharp
// In Program.cs — after AddRedisClient("redis")
builder.Services.AddNovaDistributedLocking();
```

Registered as singleton (same as `ICacheService`) — `IConnectionMultiplexer` is thread-safe and singleton.

---

## Redis Cache — Production Deployment

### Dev vs production

| | Dev (local) | Production |
|---|---|---|
| How Redis is provisioned | Aspire pulls a Docker container automatically | Managed service (Azure Cache for Redis, AWS ElastiCache, self-hosted) |
| Connection string source | Aspire service discovery (via `WithReference(redis)`) or `ConnectionStrings:redis = "localhost:6379"` | `ConnectionStrings:redis` in `appsettings.json` — **encrypted** |
| Auth | None (dev container has no password) | Password required — include in connection string |
| TLS | None | Required — `ssl=true` in connection string |
| Aspire AppHost | Used | Not used — Aspire is dev-only |

### Production connection string

StackExchange.Redis connection string format for production:

```
redis.yourdomain.com:6380,password=your-redis-password,ssl=true,abortConnect=false
```

| Option | Why |
|---|---|
| Port `6380` | Standard TLS port for Redis (Azure Cache for Redis, ElastiCache TLS) |
| `password=...` | Required for any Redis instance not on a private loopback |
| `ssl=true` | Encrypts the connection — required whenever Redis is not on localhost |
| `abortConnect=false` | Prevents the app from failing to start if Redis is temporarily unavailable at boot |

**Encrypt the connection string** using the same `ICipherService` pattern as DB connections — never store it in plaintext in `appsettings.json`.

### Steps to move to production

1. **Provision a Redis instance** — Azure Cache for Redis (Basic C0 for low-traffic, Standard C1+ for HA) or AWS ElastiCache. Enable TLS and set a strong password.

2. **Build the connection string** in the format above. Test it manually with `redis-cli` before encrypting.

3. **Encrypt the connection string** using the `ICipherService` encrypt utility with the production `ENCRYPTION_KEY`. The output is the value stored in `appsettings.json → ConnectionStrings:redis`.

4. **Set `ENCRYPTION_KEY`** in the production environment (environment variable, Azure Key Vault secret reference, or AWS Secrets Manager). The app throws at startup if the key is missing.

5. **Remove Aspire from the production deployment** — the AppHost project is not deployed. The service project (`Nova.Shell.Api`) is deployed directly. `AddRedisClient("redis")` reads `ConnectionStrings:redis` from `appsettings.json` when Aspire is not present.

6. **Review `opsettings.json` cache profiles** for production TTL values and confirm `GloballyEnabled: true`, `EmergencyDisable: false`.

7. **Monitor `/health/redis`** — add it to your uptime monitoring. If it returns `503`, the API is serving all cache-aside data from the source of truth (DB), which will increase DB load.

### Redis configuration for production

| Setting | Recommended value | Why |
|---|---|---|
| `maxmemory` | Set a limit (e.g. 75% of instance RAM) | Prevents Redis from consuming all memory |
| `maxmemory-policy` | `allkeys-lru` | Evicts least-recently-used keys when memory is full — correct for a cache |
| Persistence (RDB/AOF) | Optional for cache | Cache data is reproduced on miss; persistence is not strictly required but aids warm restarts |
| Eviction notification | Disabled by default | Enable only if the app needs to react to TTL expiry events |

### `opsettings.json` — cache kill switches (hot-reloadable, no restart)

| Switch | Use case |
|---|---|
| `Caching.GloballyEnabled: false` | Disable all caching immediately (e.g. suspected stale data incident) |
| `Caching.EmergencyDisable: true` | Same effect — separate flag so `GloballyEnabled` retains its intended state |
| `Caching.DryRunMode: true` | Cache reads/writes happen but cached data is never served — useful for validating that cache invalidation is working without affecting users |
| `Caching.Profiles.{Name}.Enabled: false` | Disable caching for one profile only, leaving others active |

---

## Configuration Files

| File | Hot-reload | Contains |
|---|---|---|
| `appsettings.json` | No (restart required) | DB connections, Redis connection string, tenant registry, JWT, OTel endpoint |
| `opsettings.json` | Yes | Log levels, log windows, rate limiting, cache profiles and kill switches |

Connection strings (DB and Redis) and the JWT secret in `appsettings.json` are **encrypted** using `ICipherService`. Never store plaintext credentials.

### Documentation rule for `opsettings.json`

Every setting added to `opsettings.json` must have an inline comment (or matching XML doc on the C# property) explaining:
- What the setting does
- What values are valid
- What the default means

```json
"RateLimiting": {
  "Enabled": true,          // false = bypass entirely for emergency disable — no restart needed
  "PermitLimit": 100,       // max requests per tenant per window (must be >= 1)
  "WindowSeconds": 60,      // window duration in seconds (must be >= 1)
  "QueueLimit": 0           // 0 = reject immediately; > 0 = queue this many before rejecting
}
```

A setting without a comment is undocumented configuration — an operator cannot safely change it without reading the source code.

---

## XML Documentation on Shared Library APIs

All `public` types and members in `Nova.Shared` and `Nova.Shared.Web` require XML documentation (`/// <summary>`).

These two projects are consumed by every domain service in the platform. XML docs are what IntelliSense shows when a developer in `Nova.Bookings.Api` types `connectionFactory.` or `dialect.`. Without them, developers must navigate to the source file to understand contract, exceptions, and assumptions.

**Required on:**
- All `public` classes, records, interfaces, enums
- All `public` methods, properties, and extension methods
- All constructor parameters where the name is not self-explanatory

**Not required on:**
- `private` and `internal` members
- `private sealed record` request/response types inside domain service endpoints
- Local variables and parameters

**Minimum bar — each `<summary>` must answer:**
- What does this do?
- What does it return / what does it write?
- What throws (if anything non-obvious)?

```csharp
/// <summary>
/// Creates and opens a database connection scoped to the specified tenant.
/// The connection string is decrypted via <see cref="ICipherService"/> before use.
/// </summary>
/// <returns>An open <see cref="IDbConnection"/>. Caller must dispose.</returns>
/// <exception cref="InvalidOperationException">
/// Thrown if the tenant's <see cref="DbType"/> is not supported.
/// </exception>
public IDbConnection CreateForTenant(TenantContext tenant) { ... }
```

The rule applies to **new additions** — do not go back and retrofit docs to existing members in the same PR. Fix forward.

---

## API Versioning

All business endpoints are versioned using URL segment versioning (`/api/v{n}/resource`).

The version set and route group are created in `Program.cs` after `builder.Build()`:

```csharp
var versionSet = app.NewApiVersionSet("Nova")
    .HasApiVersion(new ApiVersion(1, 0))
    .ReportApiVersions()
    .Build();

RouteGroupBuilder v1 = app.MapGroup("/api/v{version:apiVersion}")
    .WithApiVersionSet(versionSet)
    .MapToApiVersion(new ApiVersion(1, 0));
```

Every versioned endpoint's `Map()` method accepts `RouteGroupBuilder` and uses a **relative** route:

```csharp
public static void Map(RouteGroupBuilder group)
{
    group.MapPost("/echo", Handle)    // resolves to POST /api/v1/echo at runtime
         .RequireAuthorization()
         .WithName("Echo");
}
```

**What gets versioned:** all business API endpoints (`/hello-world`, `/echo`, `/echo/list`).  
**What stays unversioned:** health checks (`/health/**`) and diagnostic endpoints (`/test-db/**`).

Unsupported version requests (e.g. `/api/v99/echo`) return `400 Bad Request` with Problem Details.

---

## Current Endpoints

| Endpoint | Auth | Versioned | Purpose |
|---|---|---|---|
| `GET /api/v1/hello-world` | None | Yes | Liveness check — confirms the API is running and returning snake_case JSON |
| `POST /api/v1/echo` | JWT required | Yes | **REFERENCE** — demonstrates validation convention and Problem Details. Delete when cloning. |
| `POST /api/v1/echo/list` | JWT required | Yes | **REFERENCE** — demonstrates pagination contract (`PagedRequest` / `PagedResult<T>`). Delete when cloning. |
| `GET /api/v1/http-ping` | None | Yes | **REFERENCE** — demonstrates resilient outbound HTTP call via `IHttpClientFactory`. Delete when cloning. |
| `GET /test-db/mssql` | None | No | DB connectivity check against `DiagnosticConnections:MsSql` |
| `GET /test-db/postgres` | None | No | DB connectivity check against `DiagnosticConnections:Postgres` |
| `GET /test-cache` | None | No | Redis cache round-trip — stores/retrieves a value |
| `DELETE /test-cache` | None | No | Invalidates the test cache entry |
| `GET /test-lock` | None | No | Acquires and releases a distributed lock |
| `GET /test-internal-auth/token` | None | No | Returns the current outbound service JWT (for inspection) |
| `GET /test-internal-auth/protected` | InternalService | No | Protected endpoint — accepts only InternalJwt tokens |
| `GET /test-internal-auth/call-self` | None | No | Full round-trip: generates token → calls protected endpoint on itself |
| `GET /health` | None | No | Aggregate health check (MSSQL + Postgres + MariaDB + Redis) |
| `GET /health/mssql` | None | No | MSSQL health check only |
| `GET /health/postgres` | None | No | Postgres health check only |
| `GET /health/mariadb` | None | No | MariaDB health check only |
| `GET /health/redis` | None | No | Redis health check only |

Listen port: `5100` (dev) configured in `Properties/launchSettings.json`.

---

## Cloning to Create a New Domain Service

1. Copy `src/services/Nova.Shell.Api/` → `src/services/Nova.{Domain}.Api/`
2. Rename the `.csproj` and update `<RootNamespace>` and `<AssemblyName>`
3. Add to `novadhruv.slnx`
4. Add to `src/host/Nova.AppHost/Program.cs`: `builder.AddProject<Projects.Nova_{Domain}_Api>("{domain}")`
5. Delete: `EchoEndpoint.cs`, `EchoListEndpoint.cs`, `HttpPingEndpoint.cs`, `TestDbMsSqlEndpoint.cs`, `TestDbPostgresEndpoint.cs`, `TestCacheEndpoint.cs`, `TestLockEndpoint.cs`, `TestInternalAuthEndpoint.cs`, `HelloWorldEndpoint.cs`
6. Delete: `MsSqlHealthCheck.cs`, `PostgresHealthCheck.cs`, `MariaDbHealthCheck.cs` (re-add only the DB types your domain uses)
7. Add your domain endpoints following the patterns in `EchoEndpoint.cs`
8. Update `appsettings.json` — change `OpenTelemetry:ServiceName` to your service name

Keep: `Program.cs` startup order (just add/remove health checks for your DB types), all middleware, `opsettings.json` structure.

---

## See Also

| File | Purpose |
|---|---|
| `src/services/Nova.Shell.Api/docs/shell-api-postman-testing.md` | How to run and Postman testing guide |
| `planning/ainotes/conversation-context.md` | Architecture decisions and full project context |
| `planning/ainotes/acceptance-criteria.md` | Verification checklist |
| `src/services/Nova.Shell.Api/Endpoints/EchoEndpoint.cs` | Annotated reference: validation + Problem Details |
| `src/services/Nova.Shell.Api/Endpoints/EchoListEndpoint.cs` | Annotated reference: pagination contract |
| `src/services/Nova.Shell.Api/Endpoints/HttpPingEndpoint.cs` | Annotated reference: resilient HttpClient |
| `src/services/Nova.Shell.Api/Endpoints/TestInternalAuthEndpoint.cs` | Dev diagnostic: service-to-service auth |
| `docs/distributed-locking-guide.md` | 14-section guide to distributed locking (new devs) |
