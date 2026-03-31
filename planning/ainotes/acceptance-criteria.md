# Nova — Architecture Acceptance Criteria

## Instructions for Claude Code

You are verifying that the Nova codebase conforms to the agreed architecture.
Work through every section below. For each check:
- Read the relevant file(s) using Read/Grep/Glob.
- Mark the item **PASS**, **FAIL**, or **MISSING** (file does not exist).
- For every FAIL or MISSING, state the file path and describe the violation precisely.
- At the end, print a summary table: total PASS / FAIL / MISSING counts and an overall verdict.

Do not fix anything — only verify and report.

Repo root is the directory that contains `novadhruv.slnx`.

---

## 1. Solution & Project Structure

- [ ] `novadhruv.slnx` exists at repo root
- [ ] `src/shared/Nova.Shared/Nova.Shared.csproj` exists
- [ ] `src/shared/Nova.Shared.Web/Nova.Shared.Web.csproj` exists
- [ ] `src/services/Nova.Shell.Api/Nova.Shell.Api.csproj` exists
- [ ] `Nova.Shared.Web.csproj` references `Nova.Shared` via `<ProjectReference>`
- [ ] `Nova.Shell.Api.csproj` references both `Nova.Shared` and `Nova.Shared.Web` via `<ProjectReference>` (not NuGet)

---

## 2. csproj Settings

### Nova.Shared.csproj

- [ ] `<TargetFramework>net10.0</TargetFramework>`
- [ ] `<Nullable>enable</Nullable>`
- [ ] `<ImplicitUsings>enable</ImplicitUsings>`
- [ ] `<LangVersion>latest</LangVersion>`
- [ ] `<RootNamespace>Nova.Shared</RootNamespace>`
- [ ] No `<FrameworkReference Include="Microsoft.AspNetCore.App" />` — must be pure .NET
- [ ] PackageReference: `Dapper` version `2.*`
- [ ] PackageReference: `Microsoft.Data.SqlClient` version `6.*`
- [ ] PackageReference: `Npgsql` version `9.*`
- [ ] PackageReference: `MySqlConnector` version `2.*`
- [ ] PackageReference: `Serilog.AspNetCore` version `9.*`
- [ ] PackageReference: `Serilog.Sinks.File` version `6.*`
- [ ] PackageReference: `OpenTelemetry.Extensions.Hosting` version `1.*`
- [ ] PackageReference: `OpenTelemetry.Exporter.OpenTelemetryProtocol` version `1.*`
- [ ] PackageReference: `OpenTelemetry.Instrumentation.Runtime` version `1.*`
- [ ] PackageReference: `Microsoft.Extensions.Diagnostics.HealthChecks` version `10.*`
- [ ] PackageReference: `Microsoft.Extensions.Options.DataAnnotations` version `10.*`
- [ ] No `OpenTelemetry.Instrumentation.AspNetCore` or `OpenTelemetry.Instrumentation.Http` (these belong in Nova.Shared.Web)
- [ ] No `Microsoft.AspNetCore.Authentication.JwtBearer` (belongs in Nova.Shared.Web)
- [ ] No reference to `Microsoft.EntityFrameworkCore` or any EF package
- [ ] No reference to `MediatR`
- [ ] No reference to `AutoMapper`

### Nova.Shared.Web.csproj

- [ ] `<TargetFramework>net10.0</TargetFramework>`
- [ ] `<Nullable>enable</Nullable>`
- [ ] `<ImplicitUsings>enable</ImplicitUsings>`
- [ ] `<LangVersion>latest</LangVersion>`
- [ ] `<RootNamespace>Nova.Shared.Web</RootNamespace>`
- [ ] `<FrameworkReference Include="Microsoft.AspNetCore.App" />` present
- [ ] PackageReference: `Microsoft.AspNetCore.Authentication.JwtBearer` version `10.*`
- [ ] PackageReference: `OpenTelemetry.Instrumentation.AspNetCore` version `1.*`
- [ ] PackageReference: `OpenTelemetry.Instrumentation.Http` version `1.*`
- [ ] ProjectReference to `Nova.Shared.csproj`
- [ ] No reference to EF Core, MediatR, or AutoMapper

### Nova.Shell.Api.csproj

- [ ] `<TargetFramework>net10.0</TargetFramework>`
- [ ] `<Nullable>enable</Nullable>`
- [ ] `<ImplicitUsings>enable</ImplicitUsings>`
- [ ] `<LangVersion>latest</LangVersion>`
- [ ] `<RootNamespace>Nova.Shell.Api</RootNamespace>`
- [ ] ProjectReference to both `Nova.Shared.csproj` and `Nova.Shared.Web.csproj`
- [ ] No direct package references to `Dapper`, `Microsoft.Data.SqlClient`, or `Npgsql` (inherited via project reference)
- [ ] No reference to EF Core, MediatR, or AutoMapper

---

## 3. Banned Libraries & Patterns (entire codebase)

Search all `.cs` files for each of the following — all must be absent:

- [ ] No `using Microsoft.EntityFrameworkCore` anywhere
- [ ] No `DbContext` class or inheritance
- [ ] No `using MediatR` anywhere
- [ ] No `using AutoMapper` anywhere
- [ ] No `DateTime.Now` — only `DateTime.UtcNow` or `DateTimeOffset.UtcNow` permitted
- [ ] No string interpolation inside SQL queries (e.g. `$"SELECT ... {variable}"`) — all SQL must use parameterised inputs
- [ ] No generic repository pattern (e.g. `IRepository<T>`, `Repository<T>`)
- [ ] No controller classes (no `[ApiController]`, no `ControllerBase` inheritance) — Minimal API only
- [ ] No `Environment.GetEnvironmentVariable("ENCRYPTION_KEY")` calls outside `Nova.Shared/Security/CipherService.cs`
- [ ] No plaintext connection strings or JWT secrets hardcoded anywhere in `.cs` files

---

## 4. Language & Naming Conventions

- [ ] All identifiers use English UK spelling. Spot-check: look for `Initialize` (should be `Initialise`), `Color` (should be `Colour`), `Authorize` (should be `Authorise`). Grep for these American spellings and flag any found in project source files (excluding `cipher.cs`).
- [ ] File-scoped namespaces used throughout (i.e. `namespace Foo.Bar;` not `namespace Foo.Bar { }`)
- [ ] Records used for DTOs and value objects (verify `TenantContext`, `TenantRecord`, `OutboxMessage`, `CacheProfile`, `LoggingWindow` are declared as `record` or `sealed record`)
- [ ] `var` is not used where the type is non-obvious from the right-hand side (spot-check a few files)

---

## 5. Nova.Shared — File Checklist

Verify each file exists at the path shown (relative to `src/shared/Nova.Shared/`):

- [ ] `Security/cipher.cs` — must exist; must not be modified (no `namespace` wrapper added, no method signatures changed)
- [ ] `Security/CipherService.cs`
- [ ] `Security/ICipherService.cs`
- [ ] `Caching/CacheProfile.cs`
- [ ] `Caching/CacheSettings.cs`
- [ ] `Caching/ICacheService.cs`
- [ ] `Configuration/AppSettings.cs`
- [ ] `Configuration/OpsSettings.cs`
- [ ] `Configuration/OpsSettingsValidator.cs`
- [ ] `Configuration/ConfigurationExtensions.cs`
- [ ] `Data/DbType.cs`
- [ ] `Data/ISqlDialect.cs`
- [ ] `Data/MsSqlDialect.cs`
- [ ] `Data/PostgresDialect.cs`
- [ ] `Data/MySqlDialect.cs`
- [ ] `Data/IDbConnectionFactory.cs`
- [ ] `Data/DbConnectionFactory.cs`
- [ ] `Data/SafeReaderExtensions.cs`
- [ ] `Logging/LoggingWindow.cs`
- [ ] `Logging/TimeWindowLevelEvaluator.cs`
- [ ] `Logging/SerilogSetupExtensions.cs`
- [ ] `Messaging/Outbox/OutboxMessage.cs`
- [ ] `Observability/OtelSetupExtensions.cs`
- [ ] `Tenancy/TenantContext.cs`
- [ ] `Tenancy/TenantRecord.cs`
- [ ] `Tenancy/TenantRegistry.cs`

**Files that must NOT exist in Nova.Shared** (they belong in Nova.Shared.Web):
- [ ] No `Auth/JwtSetupExtensions.cs` in Nova.Shared
- [ ] No `Middleware/` folder in Nova.Shared
- [ ] No `Tenancy/TenancyExtensions.cs` in Nova.Shared

---

## 5b. Nova.Shared.Web — File Checklist

Verify each file exists at the path shown (relative to `src/shared/Nova.Shared.Web/`):

- [ ] `Auth/JwtSetupExtensions.cs`
- [ ] `Middleware/CorrelationIdMiddleware.cs`
- [ ] `Middleware/TenantResolutionMiddleware.cs`
- [ ] `Observability/WebOtelExtensions.cs`
- [ ] `Tenancy/TenancyExtensions.cs`

---

## 6. Nova.Shared — Component Detail Checks

### 6.1 CipherService

- [ ] `ICipherService` declares exactly two methods: `Encrypt(string plainText)` and `Decrypt(string cipherText)`
- [ ] `CipherService` implements `ICipherService` and is `sealed`
- [ ] `ENCRYPTION_KEY` is read via `Environment.GetEnvironmentVariable("ENCRYPTION_KEY")` in the constructor
- [ ] Constructor throws `InvalidOperationException` if the environment variable is null or empty
- [ ] `CipherService` does not implement any encryption algorithm directly — it delegates to methods in `cipher.cs`
- [ ] `CipherService` is registered as `services.AddSingleton<ICipherService, CipherService>()`
- [ ] XML `<summary>` doc present on `ICipherService`, `Encrypt`, and `Decrypt`

### 6.2 ISqlDialect

- [ ] Interface declares: `TableRef(string databaseOrSchema, string table)`, `PaginationClause(int skip, int take)`, `ReturningIdClause()`, `BooleanLiteral(bool value)`, `string ParameterPrefix { get; }`
- [ ] `MsSqlDialect.TableRef` returns `"{databaseOrSchema}.dbo.{table}"` format
- [ ] `PostgresDialect.TableRef` returns `"{databaseOrSchema}.{table}"` format (no `.dbo.`)
- [ ] `MySqlDialect.TableRef` returns `"{databaseOrSchema}.{table}"` format (same as Postgres)
- [ ] `MsSqlDialect.ReturningIdClause()` returns a string containing `OUTPUT INSERTED.SeqNo`
- [ ] `PostgresDialect.ReturningIdClause()` returns a string containing `RETURNING id`
- [ ] `MySqlDialect.ReturningIdClause()` returns a string containing `LAST_INSERT_ID()`
- [ ] `MsSqlDialect.BooleanLiteral(true)` returns `"1"`, `BooleanLiteral(false)` returns `"0"`
- [ ] `PostgresDialect.BooleanLiteral(true)` returns `"true"`, `BooleanLiteral(false)` returns `"false"`
- [ ] `MySqlDialect.BooleanLiteral(true)` returns `"1"`, `BooleanLiteral(false)` returns `"0"`
- [ ] XML `<summary>` doc on interface and all members

### 6.3 IDbConnectionFactory / DbConnectionFactory

- [ ] `IDbConnectionFactory` declares `CreateForTenant(TenantContext tenant)` and `CreateFromConnectionString(string connectionString, DbType dbType)`
- [ ] Both return `IDbConnection`
- [ ] `DbConnectionFactory.CreateForTenant` calls `ICipherService.Decrypt(tenant.ConnectionString)` before opening
- [ ] `DbConnectionFactory.CreateFromConnectionString` calls `ICipherService.Decrypt(connectionString)` before opening
- [ ] For `DbType.MsSql`, creates a `Microsoft.Data.SqlClient.SqlConnection`
- [ ] For `DbType.Postgres`, creates an `Npgsql.NpgsqlConnection`
- [ ] For `DbType.MySql`, creates a `MySqlConnector.MySqlConnection`
- [ ] Decryption is not performed anywhere else (e.g. not in `TenantRegistry`)
- [ ] XML `<summary>` doc on interface and members

### 6.4 TenantContext

- [ ] Declared as `public sealed record TenantContext`
- [ ] Properties: `TenantId` (string), `ConnectionString` (string, encrypted), `DbType` (DbType enum), `SchemaVersion` (string)
- [ ] All properties use `{ get; init; }`
- [ ] XML `<summary>` doc on type and all properties

### 6.5 DbType enum

- [ ] Declared as `public enum DbType`
- [ ] Contains exactly three members: `MsSql`, `Postgres`, and `MySql`
- [ ] XML `<summary>` doc on type and members

### 6.6 TenantRegistry

- [ ] Loads tenant records from `IOptions<AppSettings>` (not from a database)
- [ ] Provides a lookup method to find a `TenantRecord` by `TenantId`
- [ ] Does NOT decrypt connection strings (decryption belongs in `DbConnectionFactory`)
- [ ] XML `<summary>` doc on type and public members

### 6.7 (Moved to Nova.Shared.Web — see section 6b.2)

TenantResolutionMiddleware is in `Nova.Shared.Web.Middleware`. Verify it is NOT in `Nova.Shared.Middleware`.

### 6.8 (Moved to Nova.Shared.Web — see section 6b.3)

CorrelationIdMiddleware is in `Nova.Shared.Web.Middleware`. Verify it is NOT in `Nova.Shared.Middleware`.

### 6.9 SafeReaderExtensions

- [ ] Extension methods on `IDataReader`
- [ ] Includes: `GetStringSafe`, `GetInt32Safe`, `GetDateTimeSafe`, `GetBoolSafe`, `GetGuidSafe`
- [ ] All return a safe default (not throw) when the column is `DBNull`
- [ ] `GetStringSafe` returns `string.Empty` (not null) for DBNull
- [ ] `GetInt32Safe` returns `0` for DBNull
- [ ] XML `<summary>` doc on all methods

### 6.10 OutboxMessage

- [ ] Declared as `public sealed record OutboxMessage`
- [ ] Contains all required properties: `Id`, `TenantId`, `CreatedOn`, `ScheduledOn`, `ProcessedOn`, `Exchange`, `RoutingKey`, `Payload`, `ContentType`, `RetryCount`, `MaxRetries`, `LastError`, `Status`, `CorrelationId`
- [ ] `Id` is `string` (not `Guid`) — stores UUID v7 as string
- [ ] Nullable properties: `ScheduledOn`, `ProcessedOn`, `LastError`, `CorrelationId`
- [ ] No relay/dispatch logic present — record definition only

### 6.11 AppSettings

- [ ] Strongly-typed root class matching `appsettings.json` structure
- [ ] Sections: `DiagnosticConnections` (MsSql, Postgres strings), `Tenants` (array of `TenantRecord`), `Jwt` (Issuer, Audience, SecretKey), `OpenTelemetry` (ServiceName, OtlpEndpoint)
- [ ] XML `<summary>` doc on type and all properties

### 6.12 OpsSettings

- [ ] Strongly-typed root class matching `opsettings.json` structure
- [ ] `Logging` section: `DefaultLevel`, `EnableRequestResponseLogging`, `EnableDiagnosticLogging`, `Windows` (array of `LoggingWindow`)
- [ ] `Caching` section: `GloballyEnabled`, `EmergencyDisable`, `DryRunMode`, `Profiles` (dictionary), `EndpointExclusions` (array)
- [ ] XML `<summary>` doc on type and all properties

### 6.13 OpsSettings Hot-Reload

- [ ] `OpsSettings` bound via `IOptionsMonitor<OpsSettings>`
- [ ] A hosted service (`OpsSettingsWatcher` or equivalent) subscribes to `IOptionsMonitor.OnChange`
- [ ] On change: validates via `OpsSettingsValidator` (or `IValidateOptions<OpsSettings>`)
- [ ] If validation passes: updates last-known-good reference, logs success message
- [ ] If validation fails: retains previous settings, logs warning with error details — does NOT crash or apply invalid config
- [ ] `IOpsSettingsAccessor` (or equivalent) wraps the last-known-good instance and is what other components inject

### 6.14 OpsSettingsValidator

- [ ] Implements `IValidateOptions<OpsSettings>`
- [ ] Validates at minimum: `DefaultLevel` is a valid Serilog log level, `Windows` entries have valid time formats and log levels

### 6.15 LoggingWindow / TimeWindowLevelEvaluator

- [ ] `LoggingWindow` is a `record` with `Name`, `Start` (string HH:mm), `End` (string HH:mm), `Level` (string)
- [ ] `TimeWindowLevelEvaluator` checks current UTC time against active windows and returns the highest-priority level in effect

### 6.16 SerilogSetupExtensions

- [ ] Configures two file sinks: `logs/audit-.log` (Info+) and `logs/debug-.log` (Debug+)
- [ ] Debug sink is only active when `EnableDiagnosticLogging` is `true`
- [ ] Log output is structured JSON
- [ ] Enrichers configured: `FromLogContext`, correlation ID enricher, tenant ID enricher
- [ ] On startup, logs `[Logging] DB sink disabled — enable in opsettings` (no DB sink in shell)
- [ ] `TimeWindowLevelEvaluator` applied on startup and on opsettings reload

### 6.17 (Moved to Nova.Shared.Web — see section 6b.1)

`JwtSetupExtensions` is in `Nova.Shared.Web.Auth`. Verify it is NOT in `Nova.Shared.Auth`.

### 6.18 OtelSetupExtensions (Nova.Shared — base only)

- [ ] Registers a custom `ActivitySource` named `"Nova.Shell"` as singleton in DI
- [ ] Adds trace source `"Nova.Shell"` and OTLP exporter
- [ ] Adds metrics: `AddRuntimeInstrumentation` and OTLP exporter
- [ ] Does NOT add `AddAspNetCoreInstrumentation` or `AddHttpClientInstrumentation` (those are in `WebOtelExtensions`)
- [ ] OTLP exporter uses `OpenTelemetry:OtlpEndpoint` from appsettings
- [ ] Resource attributes include `service.name`, `service.version`, `deployment.environment`

### 6.19 CacheProfile / ICacheService

- [ ] `CacheProfile` is a record with at minimum: `Layer`, `TtlSeconds`, `Enabled`
- [ ] `ICacheService` declares `GetOrSetAsync` and `InvalidateAsync` (interface/skeleton only — no implementation required)
- [ ] No Redis, `IMemoryCache`, or response cache implementation present (deferred)

### 6.20 XML Documentation

- [ ] Every public type in `Nova.Shared` has an XML `<summary>` comment
- [ ] Every public method and property in `Nova.Shared` has an XML `<summary>` comment
- [ ] `cipher.cs` is exempt (third-party file, do not modify)

---

## 6b. Nova.Shared.Web — Component Detail Checks

### 6b.1 JwtSetupExtensions

- [ ] Namespace is `Nova.Shared.Web.Auth`
- [ ] Calls `ICipherService.Decrypt(AppSettings.Jwt.SecretKey)` to obtain the plaintext secret
- [ ] Configures `AddJwtBearer` with issuer, audience, and the decrypted signing key
- [ ] Decryption of the JWT secret happens only here — not in `AppSettings` or `TenantRegistry`

### 6b.2 TenantResolutionMiddleware

- [ ] Namespace is `Nova.Shared.Web.Middleware`
- [ ] Reads `tenant_id` claim from the JWT
- [ ] Looks up the tenant in `TenantRegistry`
- [ ] Sets a scoped `TenantContext` in DI (or `HttpContext.Items`)
- [ ] Does not throw for unauthenticated requests — allows pipeline to continue (auth endpoints handle 401)

### 6b.3 CorrelationIdMiddleware

- [ ] Namespace is `Nova.Shared.Web.Middleware`
- [ ] Reads `X-Correlation-ID` header from incoming request
- [ ] If absent, generates a new correlation ID (e.g. `Guid.NewGuid().ToString()`)
- [ ] Sets the correlation ID on the response header `X-Correlation-ID`
- [ ] Makes the correlation ID available for log enrichment and endpoint use

### 6b.4 TenancyExtensions (Nova.Shared.Web)

- [ ] Namespace is `Nova.Shared.Web.Tenancy`
- [ ] Registers `IHttpContextAccessor`
- [ ] Registers scoped `TenantContext` resolved from `HttpContext.Items`
- [ ] Exposes a constant key used by `TenantResolutionMiddleware` to store the context

### 6b.5 WebOtelExtensions

- [ ] Namespace is `Nova.Shared.Web.Observability`
- [ ] Extends an existing OTel registration — uses `ConfigureOpenTelemetryTracerProvider` and `ConfigureOpenTelemetryMeterProvider` (does not call `AddOpenTelemetry()` again)
- [ ] Adds traces: `AddAspNetCoreInstrumentation`, `AddHttpClientInstrumentation`
- [ ] Adds metrics: `AddAspNetCoreInstrumentation`

---

## 7. Nova.Shell.Api — File Checklist

Verify each file exists at the path shown (relative to `services/Nova.Shell.Api/`):

- [ ] `Program.cs`
- [ ] `appsettings.json`
- [ ] `opsettings.json`
- [ ] `Endpoints/HelloWorldEndpoint.cs`
- [ ] `Endpoints/TestDbMsSqlEndpoint.cs`
- [ ] `Endpoints/TestDbPostgresEndpoint.cs`
- [ ] `Endpoints/TestDbMySqlEndpoint.cs`
- [ ] `HealthChecks/MsSqlHealthCheck.cs`
- [ ] `HealthChecks/PostgresHealthCheck.cs`
- [ ] `HealthChecks/MySqlHealthCheck.cs`

---

## 8. Nova.Shell.Api — Component Detail Checks

### 8.1 Program.cs — Startup Order

Verify the following are wired in this order:

- [ ] `AddNovaConfiguration` (from `Nova.Shared`) — loads `appsettings.json` and `opsettings.json`, validates OpsSettings
- [ ] `AddNovaLogging` (from `Nova.Shared`) — Serilog with file sinks
- [ ] `AddNovaTenancy` (from `Nova.Shared.Web`) — TenantRegistry + TenantContext DI
- [ ] `AddNovaOpenTelemetry` (from `Nova.Shared`) — base OTel: runtime metrics, OTLP exporter, ActivitySource
- [ ] `AddNovaWebInstrumentation` (from `Nova.Shared.Web`) — ASP.NET Core traces + metrics instrumentation
- [ ] JWT authentication (`AddNovaJwt` from `Nova.Shared.Web`)
- [ ] Health checks (both MSSQL and Postgres)
- [ ] Endpoint/route registration

### 8.2 Program.cs — Console Mode

- [ ] `RUN_AS_CONSOLE=true` environment variable OR `--console` command-line arg switches to console mode
- [ ] Console mode calls `UseConsoleLifetime()` (or equivalent)
- [ ] Console mode prints to stdout:
  - [ ] Config files loaded and validated
  - [ ] MSSQL connectivity result (ping attempt)
  - [ ] Postgres connectivity result (ping attempt)
  - [ ] Tenant registry loaded (count of tenants)
  - [ ] OTel configured (OTLP endpoint printed)

### 8.3 Program.cs — Middleware Pipeline

- [ ] `CorrelationIdMiddleware` added to pipeline
- [ ] `TenantResolutionMiddleware` added to pipeline
- [ ] Both are added before endpoint execution

### 8.4 appsettings.json

- [ ] Contains `DiagnosticConnections` section with `MsSql`, `Postgres`, and `MySql` string keys
- [ ] Contains `Tenants` array with at minimum one example entry containing: `TenantId`, `DisplayName`, `DbType`, `ConnectionString`, `SchemaVersion`
- [ ] Contains `Jwt` section with `Issuer`, `Audience`, `SecretKey`
- [ ] Contains `OpenTelemetry` section with `ServiceName` and `OtlpEndpoint`
- [ ] No plaintext passwords, connection string credentials, or JWT secrets — values must be placeholder-encrypted strings (not empty, not actual credentials)

### 8.5 opsettings.json

- [ ] Contains `Logging` section with `DefaultLevel`, `EnableRequestResponseLogging`, `EnableDiagnosticLogging`, `Windows` array
- [ ] `Windows` array contains at least one example window with `Name`, `Start`, `End`, `Level`
- [ ] Contains `Caching` section with `GloballyEnabled`, `EmergencyDisable`, `DryRunMode`, `Profiles`, `EndpointExclusions`

### 8.6 GET /hello-world

- [ ] No authentication required (`AllowAnonymous` or no `RequireAuthorization` call)
- [ ] Returns HTTP 200 JSON with `message`, `timestamp`, `correlationId` fields
- [ ] `timestamp` uses UTC (`DateTimeOffset.UtcNow` or `DateTime.UtcNow`)
- [ ] `correlationId` sourced from the value set by `CorrelationIdMiddleware`
- [ ] Registered as a Minimal API route (`app.MapGet(...)`)

### 8.7 GET /test-db/mssql

- [ ] No tenant resolution — uses `DiagnosticConnections:MsSql` directly
- [ ] Uses `IDbConnectionFactory.CreateFromConnectionString(...)` with `DbType.MsSql`
- [ ] Executes exactly: `SELECT code, value FROM sales97.dbo.pointer` (built via `MsSqlDialect.TableRef("sales97", "pointer")`)
- [ ] Uses Dapper (`QueryAsync` or equivalent) — no raw `IDataReader` loop unless `SafeReaderExtensions` used
- [ ] Returns HTTP 200 JSON array of `{ code, value }` objects on success
- [ ] Returns HTTP 503 with `{ "error": "...", "db": "mssql" }` on connection/query failure
- [ ] No string interpolation in the SQL query

### 8.8 GET /test-db/postgres

- [ ] No tenant resolution — uses `DiagnosticConnections:Postgres` directly
- [ ] Uses `IDbConnectionFactory.CreateFromConnectionString(...)` with `DbType.Postgres`
- [ ] Executes exactly: `SELECT code, value FROM sales97.pointer` (built via `PostgresDialect.TableRef("sales97", "pointer")`)
- [ ] Returns HTTP 200 JSON array on success
- [ ] Returns HTTP 503 with `{ "error": "...", "db": "postgres" }` on failure
- [ ] No string interpolation in SQL

### 8.9 GET /test-db/mysql

- [ ] No tenant resolution — uses `DiagnosticConnections:MySql` directly
- [ ] Uses `IDbConnectionFactory.CreateFromConnectionString(...)` with `DbType.MySql`
- [ ] Executes exactly: `SELECT code, value FROM sales97.pointer` (built via `MySqlDialect.TableRef("sales97", "pointer")`)
- [ ] Returns HTTP 200 JSON array on success
- [ ] Returns HTTP 503 with `{ "error": "...", "db": "mysql" }` on failure
- [ ] No string interpolation in SQL

### 8.10 GET /health/mssql

- [ ] Mapped to ASP.NET Core health check endpoint for the `MsSqlHealthCheck`
- [ ] `MsSqlHealthCheck` executes `SELECT 1` against `DiagnosticConnections:MsSql`
- [ ] Returns standard health check JSON response (Healthy / Degraded / Unhealthy)

### 8.11 GET /health/postgres

- [ ] Mapped to ASP.NET Core health check endpoint for the `PostgresHealthCheck`
- [ ] `PostgresHealthCheck` executes `SELECT 1` against `DiagnosticConnections:Postgres`
- [ ] Returns standard health check JSON response

### 8.12 GET /health/mysql

- [ ] Mapped to ASP.NET Core health check endpoint for the `MySqlHealthCheck`
- [ ] `MySqlHealthCheck` executes `SELECT 1` against `DiagnosticConnections:MySql`
- [ ] Returns standard health check JSON response

### 8.13 GET /health

- [ ] Aggregate health check combining MSSQL, Postgres, and MySQL results
- [ ] Standard ASP.NET Core health check response format

---

## 9. Cross-Cutting Security Rules

- [ ] `ENCRYPTION_KEY` read only in `CipherService` constructor via `Environment.GetEnvironmentVariable` — grep all `.cs` files to confirm no other call site
- [ ] `ICipherService.Decrypt` called only in `DbConnectionFactory` and `JwtSetupExtensions` — no other `.cs` file calls `Decrypt` or `Encrypt` except `CipherService` itself and its tests
- [ ] No connection string appears in plaintext in any `.cs` file
- [ ] No JWT secret appears in plaintext in any `.cs` file
- [ ] All SQL uses parameterised queries (Dapper anonymous objects or `DynamicParameters`) — grep for `$"` or `string.Format` adjacent to SQL keywords

---

## 10. Audit Column Convention

This applies to domain service tables (not the shell test query, which reads legacy data as-is):

- [ ] Any `CREATE TABLE` or migration scripts (if present) include the six audit columns in this exact order after domain columns:
  1. `frz_ind` — `bit` (MSSQL) / `boolean` (Postgres), NOT NULL DEFAULT 0
  2. `created_on` — `datetime2` (MSSQL) / `timestamptz` (Postgres), NOT NULL
  3. `created_by` — `nvarchar(10)` / `varchar(10)`, NOT NULL
  4. `updated_on` — `datetime2` / `timestamptz`, NULL
  5. `updated_by` — `nvarchar(10)` / `varchar(10)`, NULL
  6. `updated_at` — `nvarchar(45)` / `varchar(45)`, NULL (stores IP address — not a datetime)
- [ ] Any Dapper queries against domain tables include `WHERE frz_ind = 0` (MSSQL) or `WHERE frz_ind = false` (Postgres)
- [ ] Note: `updated_at` column stores client IP address — verify no code writes a datetime value to it

---

## 11. Primary Key Convention

- [ ] MSSQL: PK column named `SeqNo`, type `INT IDENTITY(1,1)` — no UUID used as PK in MSSQL tables
- [ ] MySQL/MariaDB: PK column named `SeqNo`, type `INT AUTO_INCREMENT` — same column name as MSSQL
- [ ] Postgres: PK column named `id`, type `uuid` — generated via `Guid.CreateVersion7()` in application code
- [ ] In all DTOs and application-layer records, ID fields are typed as `string` (not `int` or `Guid`)
- [ ] Infrastructure layer is the only place that parses string IDs to `int` (MSSQL/MySQL) or `Guid` (Postgres)
- [ ] Grep for `Guid.NewGuid()` — should not be used for PK generation; must be `Guid.CreateVersion7()`

---

## 12. Multi-Tenancy Rules

- [ ] `TenantContext` is registered as a **scoped** service (not singleton, not transient)
- [ ] `TenantRegistry` is registered as a **singleton** (loaded once from config)
- [ ] `ICipherService` / `CipherService` is registered as a **singleton**
- [ ] `ISqlDialect` is resolved per-request based on `TenantContext.DbType` — not hardcoded
- [ ] No code accesses `HttpContext.Items["TenantContext"]` type pattern directly outside middleware — downstream code injects `TenantContext` via DI

---

## 13. Outbox / Inbox Tables (when implemented beyond the shell)

- [ ] `outbox_messages` table includes `tenant_id` column
- [ ] `inbox_messages` table includes `tenant_id` column (or equivalent idempotency table)
- [ ] `OutboxMessage.TenantId` is populated before insert — not left null

---

## Reporting Format

After completing all checks, output the following:

```
## Acceptance Criteria Report

| Section | PASS | FAIL | MISSING |
|---------|------|------|---------|
| 1. Solution Structure | x | x | x |
| 2. csproj Settings | x | x | x |
| 3. Banned Libraries | x | x | x |
| 4. Naming Conventions | x | x | x |
| 5. Nova.Shared Files | x | x | x |
| 5b. Nova.Shared.Web Files | x | x | x |
| 6. Nova.Shared Components | x | x | x |
| 6b. Nova.Shared.Web Components | x | x | x |
| 7. Nova.Shell.Api Files | x | x | x |
| 8. Nova.Shell.Api Components | x | x | x |
| 9. Security Rules | x | x | x |
| 10. Audit Columns | x | x | x |
| 11. Primary Keys | x | x | x |
| 12. Multi-Tenancy | x | x | x |
| **TOTAL** | **x** | **x** | **x** |

**Overall verdict: PASS / FAIL**

### Violations
(List each FAIL and MISSING here with: file path, check description, what was found vs. what was expected)
```
