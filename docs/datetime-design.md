# Nova Platform — Date and Time Field Design

---

## UX Contract

The UX layer defines two date/time formats. All Nova APIs must honour this contract.

| Format | Example | UX behaviour |
|---|---|---|
| `yyyy-MM-dd` | `2026-04-13` | Calendar date — **never shifted**. Displayed as-is via `formatCalendarDate()` / `fmt.calendarDate()`. |
| `yyyy-MM-ddTHH:mm:ssZ` | `2026-04-13T09:15:00Z` | Audit timestamp — **UTC input, browser-timezone display** via `formatAuditDateTime()` / `fmt.auditDateTime()`. |

**Detection rule:** presence of `T` in the string is the signal. No `T` → calendar date (no shift). Has `T` → audit timestamp (shift to user timezone).

**Outbound (UX → API):** always UTC via `toUtcIso()` which calls `DateTime.utc().toISO()`.

---

## Platform Strategy

### Three field categories

**1. Calendar date fields** — booking date, departure date, date of birth, passport expiry, etc.
- No time component. No timezone. Stored and returned exactly as entered.
- Examples: `departure_date`, `dob`, `passport_expiry_date`

**2. Time-of-day fields** — departure time, check-in time, session time, etc.
- Wall-clock time at the relevant location. Not a UTC moment. Never converted.
- Stored as `varchar(5)`, format `HH:mm` (24-hour, zero-padded), across all three databases.
- C# property: `string`.
- **Strict validation rule:** must match `HH:mm` exactly — hours `00`–`23`, minutes `00`–`59`. Zero-padding is mandatory (`09:30` is valid, `9:30` is not). Enforced at the API boundary before any database interaction.
- No TypeHandler needed — `varchar` maps to `string` natively in all three dialects.

> **Travel domain note — this rule is critical.**
> A departure time such as `19:30` is the wall-clock time printed on the ticket or boarding pass at the departure location. It is not a UTC moment and must never be treated as one. Storing it as UTC would require consumers to know the departure timezone to reconstruct the meaningful time — a source of serious errors in a travel system. Storing it as `HH:mm` string is correct precisely because it makes no timezone claim. Display-layer formatting (12-hour vs 24-hour) is the UX layer's responsibility and is locale-specific; the stored value is always 24-hour.

**3. UTC timestamp fields** — audit columns and any moment-in-time recording.
- Always UTC on the wire. UX shifts to browser timezone for display.
- Examples: `created_on`, `updated_on`, `locked_until`, `last_login_on`, `expires_on`

---

## Database Column Types

All three supported dialects must be accounted for. MySQL was previously considered dropped — that instruction is obsolete. MariaDB/MySQL is an active target dialect.

### Calendar dates

| Dialect | Column type | Notes |
|---|---|---|
| MSSQL | `date` | Stores date only, no time component |
| Postgres | `date` | Stores date only, no time component |
| MariaDB | `date` | Stores date only, no time component |

### UTC timestamps

| Dialect | Column type | Notes |
|---|---|---|
| MSSQL | `datetime2` | Modern replacement for `datetime`. Same `System.DateTime` Dapper mapping. Better precision (100 ns) and wider range (0001–9999). Legacy tables use `datetime` — mapping is identical. |
| Postgres | `timestamptz` | Stores UTC, returns offset-aware value. |
| MariaDB | `datetime` | No timezone-aware column type. UTC by convention — the application layer is solely responsible for storing and reading UTC. |

**MSSQL note:** `datetimeoffset` is NOT used. The long-term plan is to retire MSSQL (3–5 years, client-by-client via tenant registry). Using `datetime2` keeps new tables consistent with legacy `datetime` tables and avoids a divergent column type in a database whose retirement is already planned. Migration scripts (`V002`, `V003`, etc.) follow the same convention.

---

## C# Types

Shared C# record types (used across all three dialects) use a single type per category.

| Category | C# type | Nullable form |
|---|---|---|
| Calendar date | `DateOnly` | `DateOnly?` |
| UTC timestamp | `DateTimeOffset` | `DateTimeOffset?` |

`DateTimeOffset` is chosen for UTC timestamps — not `DateTime` — because:
- Postgres `timestamptz` → `DateTimeOffset` is the Npgsql 6+ default and cannot be changed without enabling legacy mode (a discouraged path).
- `DateTimeOffset` with `Offset = TimeSpan.Zero` unambiguously represents UTC with no `DateTimeKind` ambiguity.
- When MSSQL is eventually retired, no C# record changes are needed.

---

## Dapper TypeHandlers — Required per Dialect

Dapper does not natively bridge `System.DateTime` → `DateTimeOffset`, or `object` → `DateOnly`. Each service registers TypeHandlers once at startup inside `RegisterTypeHandlers()`.

### Handler: `DateOnlyTypeHandler` — all three dialects

```csharp
public sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override DateOnly Parse(object value) =>
        value switch
        {
            DateOnly d  => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _           => DateOnly.Parse(value.ToString()!)
        };

    public override void SetValue(IDbDataParameter parameter, DateOnly value) =>
        parameter.Value = value.ToString("yyyy-MM-dd");
}
```

Registration: `SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());`

Postgres (Npgsql) handles `date` → `DateOnly` natively from v7 — the handler is still safe to register but may not be invoked.

### Handler: `DateTimeOffsetTypeHandler` — MSSQL and MariaDB only

Converts the `System.DateTime` that Dapper materialises from `datetime2` / `datetime` columns into `DateTimeOffset` with UTC offset.

```csharp
public sealed class DateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override DateTimeOffset Parse(object value) =>
        value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt        => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _                  => DateTimeOffset.Parse(value.ToString()!)
        };

    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) =>
        parameter.Value = value.UtcDateTime; // write DateTime (UTC) into datetime2/datetime column
}
```

Registration (MSSQL and MariaDB services only): `SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());`

**Postgres services must NOT register this handler.** Npgsql returns `DateTimeOffset` natively for `timestamptz` — registering the handler would override Npgsql's own converter and cause materialisation errors.

---

## Parameter Passing — UtcNow Helper

`AuthDbHelper.UtcNow()` returns `DateTimeOffset.UtcNow` regardless of dialect. For MSSQL/MariaDB, the `DateTimeOffsetTypeHandler.SetValue` converts it to `DateTime` (UTC) when Dapper binds the parameter. For Postgres it passes through as `DateTimeOffset`. No special-casing in repository code.

```csharp
// Repository code — same for all dialects
new { updated_on = AuthDbHelper.UtcNow(), ... }
```

---

## Summary Table

| | MSSQL | Postgres | MariaDB |
|---|---|---|---|
| Calendar date column | `date` | `date` | `date` |
| Time-of-day column | `varchar(5)` | `varchar(5)` | `varchar(5)` |
| Timestamp column | `datetime2` (new) / `datetime` (legacy) | `timestamptz` | `datetime` |
| C# calendar date type | `DateOnly` | `DateOnly` | `DateOnly` |
| C# time-of-day type | `string` | `string` | `string` |
| C# timestamp type | `DateTimeOffset` | `DateTimeOffset` | `DateTimeOffset` |
| `DateOnlyTypeHandler` needed | Yes | Optional (Npgsql native) | Yes |
| `TimeOfDay` TypeHandler needed | **No** | **No** | **No** |
| `DateTimeOffsetTypeHandler` needed | **Yes** | **No** | **Yes** |
| UTC guarantee | Convention (app layer) | Enforced by `timestamptz` | Convention (app layer) |

---

## Migration — V001 Convention

The `V001__CreateNovaAuth.sql` files for each dialect follow this pattern:

- MSSQL: `datetime2 NOT NULL` for audit columns (not `datetimeoffset`, not `datetime`)
- Postgres: `timestamptz NOT NULL`
- MariaDB: `datetime NOT NULL`

Subsequent migration versions (`V002`, `V003`, ...) follow the same convention per dialect.

---

## Legacy MSSQL Tables

### Universal rule — `datetime` for everything

In every legacy MSSQL database, **all** date/time columns — regardless of semantic category — use the `datetime` data type. This covers:

- Calendar date columns (`DueDate`, `StartDate`, `DepDate`, `dob`, `passport_expiry_date`, …)
- Time-of-day columns (`DueTime`, `StartTime`, `EstJobTime`, departure_time, …)
- UTC timestamp columns (`CreatedOn`, `UpdatedOn`, `AssignedOn`, `DoneOn`, …)

There is no `date`, `time`, `datetime2`, or `datetimeoffset` in legacy schemas. This applies uniformly across all Nova services that read legacy MSSQL tables: **Nova.ToDo.Api** (`sales97.dbo.ToDo`), and all forthcoming services — Nova.CRM.Api, Nova.OpsBookings.Api, Nova.OpsGroups.Api, Nova.Analytics.Api.

### Dapper DTO pattern for legacy MSSQL tables

The Dapper DTO (internal row record) uses `DateTime` / `DateTime?` for **all three categories**:

```csharp
// Legacy MSSQL DTO — ALL date/time fields are DateTime regardless of semantic category
internal sealed record ToDoRow(
    DateTime  DueDate,     // calendar date — time component discarded in projection
    DateTime? DueTime,     // time-of-day   — date component discarded in projection
    DateTime? AssignedOn,  // UTC timestamp — Kind = Unspecified from ADO.NET
    ...
);
```

The projection layer (not the DTO) is responsible for converting to the correct C# API type:

| Category | DB column type | DTO type | Projection | API response type |
|---|---|---|---|---|
| Calendar date | `datetime` | `DateTime` | `DateOnly.FromDateTime(dt)` | `DateOnly` |
| Time-of-day | `datetime` | `DateTime?` | `dt.ToString("HH:mm")` | `string?` |
| UTC timestamp | `datetime` | `DateTime?` | `new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))` | `DateTimeOffset?` |

### `DateTimeOffsetTypeHandler` and legacy tables

The `DateTimeOffsetTypeHandler` is **not** involved in the legacy MSSQL DTO path. It only applies when a Dapper DTO property is typed as `DateTimeOffset` — which legacy DTOs do not use. The handler is still registered at startup (for any Nova-owned MSSQL tables in the same service that do use `DateTimeOffset` properties).

### New (Nova-owned) MSSQL tables in the same service

Nova-owned MSSQL tables created by Nova migrations (`datetime2` columns) follow the three-category design — `DateTimeOffsetTypeHandler` applies there. A single service may read from both legacy tables (DateTime DTO, manual projection) and Nova-owned tables (DateTimeOffset DTO, TypeHandler), and both patterns coexist without conflict.

### Migration path to Postgres / MariaDB

When a tenant migrates from MSSQL to Postgres or MariaDB (via tenant registry update):

- The Postgres / MariaDB schema uses correct column types (`date`, `varchar(5)`, `timestamptz` / `datetime`).
- The Dapper DTO **also changes** for the Postgres/MariaDB query path — `DateOnly` for calendar dates, `string?` for time-of-day — because the DB now returns correct types natively.
- The API response records (`ToDoDetail`, etc.) remain unchanged; they already use `DateOnly`, `string?`, `DateTimeOffset`.
- The multi-dialect DTO work (separate DTOs or dialect-normalised SQL via `CONVERT` / `CAST`) is scoped to each service's multi-dialect implementation pass — it is explicitly flagged via TODO comments in the endpoint query code.
