# ToDo API — Inline SQL: Review and Update for Multi-DB Support

All SQL in Nova.ToDo.Api was initially written for **MSSQL** (the primary legacy target). Each item below identifies SQL that must be reviewed and rewritten when a tenant migrates to Postgres or MariaDB.

The shared `ISqlDialect` provides helpers for common abstractions (pagination, active-rows filter, soft-delete clause). Items marked **ISqlDialect candidate** are good candidates for a new dialect method; items marked **manual** require bespoke per-engine branches.

---

## 1. Table References — Three-Part Naming ✅ RESOLVED

**Description:** MSSQL uses three-part `database.schema.table` names (e.g. `sales97.dbo.ToDo`). Postgres uses `schema.table` (MSSQL database-name becomes Postgres schema-name); MariaDB uses `database.table` (the `dbo` domain is dropped).

**Resolution:** All 21 endpoints now call `dialect.TableRef("sales97", "ToDo")` (and equivalent for `Priority`, `users`, `accountmast`). `ISqlDialect.TableRef(databaseOrSchema, table)` already implemented correctly in all three dialects:
- `MsSqlDialect` → `sales97.dbo.ToDo`
- `PostgresDialect` → `sales97.todo`
- `MariaDbDialect` → `` `sales97`.`todo` ``

No further code changes required for table references.

---

## 2. TOP N → LIMIT N (Pre-Edit Gets)

**Description:** Pre-edit get endpoints use `SELECT TOP 1` to return the most-recent matching open record. `TOP` is MSSQL-only. Postgres and MariaDB use `LIMIT 1` appended at the end of the query.

| Source file | Method |
|---|---|
| `Endpoints/GetByBookingEndpoint.cs` | `HandleAsync` — `SELECT TOP 1 ...` |
| `Endpoints/GetByQuoteEndpoint.cs` | `HandleAsync` — `SELECT TOP 1 ...` |
| `Endpoints/GetByTourSeriesDepartureEndpoint.cs` | `HandleAsync` — `SELECT TOP 1 ...` |
| `Endpoints/GetByTaskSourceEndpoint.cs` | `HandleAsync` — `SELECT TOP 1 ...` |
| `Endpoints/CreateToDoEndpoint.cs` | `HandleAsync` — `SELECT TOP 1 ...` in the upsert find query |

**MSSQL:** `SELECT TOP 1 ... ORDER BY SeqNo DESC`
**Postgres:** `SELECT ... ORDER BY seq_no DESC LIMIT 1`
**MariaDB:** `SELECT ... ORDER BY seq_no DESC LIMIT 1`

**ISqlDialect candidate:** Add `TopOne(string orderByClause)` or use the existing `PaginationClause(skip: 0, take: 1)`.

---

## 3. UTC Datetime Function — GETUTCDATE()

**Description:** All INSERT and UPDATE statements use `GETUTCDATE()` to set `CreatedOn`, `UpdatedOn`, and `DoneOn` server-side. This is MSSQL-only. The service also reads the new `UpdatedOn` value back immediately after update using `SELECT GETUTCDATE()`.

| Source file | Method |
|---|---|
| `Endpoints/CreateToDoEndpoint.cs` | `HandleAsync` — `CreatedOn = GETUTCDATE(), UpdatedOn = GETUTCDATE()` in INSERT |
| `Endpoints/CreateToDoEndpoint.cs` | `BuildUpdateSql` — `UpdatedOn = GETUTCDATE()` |
| `Endpoints/UpdateToDoEndpoint.cs` | `HandleAsync` — `UpdatedOn = GETUTCDATE(); SELECT GETUTCDATE();` |
| `Endpoints/DeleteToDoEndpoint.cs` | `HandleAsync` — n/a (no audit write on delete) |
| `Endpoints/FreezeToDoEndpoint.cs` | `HandleAsync` — `UpdatedOn = GETUTCDATE()` |
| `Endpoints/CompleteToDoEndpoint.cs` | `HandleAsync` — `DoneOn = GETUTCDATE(), UpdatedOn = GETUTCDATE()` |
| `Endpoints/UndoCompleteToDoEndpoint.cs` | `HandleAsync` — `UpdatedOn = GETUTCDATE()` |

**MSSQL:** `GETUTCDATE()`
**Postgres:** `NOW() AT TIME ZONE 'UTC'` or `CURRENT_TIMESTAMP AT TIME ZONE 'UTC'`
**MariaDB:** `UTC_TIMESTAMP()`

**Note:** `UpdateToDoEndpoint` reads back the new `UpdatedOn` with `SELECT GETUTCDATE()` appended to the UPDATE. For Postgres, use `RETURNING updated_on`; for MariaDB, issue a separate `SELECT UTC_TIMESTAMP()` or pass the application-side `DateTime.UtcNow` instead.

**ISqlDialect candidate:** Add `UtcNowFunction()` → `"GETUTCDATE()"` / `"NOW() AT TIME ZONE 'UTC'"` / `"UTC_TIMESTAMP()"`.

---

## 4. Identity Insert — Retrieving the Generated Primary Key

**Description:** The Create endpoint inserts a new row and immediately retrieves the generated `SeqNo` using `SELECT CAST(SCOPE_IDENTITY() AS INT)`. This is MSSQL-specific. Postgres and MariaDB have different mechanisms.

| Source file | Method |
|---|---|
| `Endpoints/CreateToDoEndpoint.cs` | `HandleAsync` — `INSERT INTO ...; SELECT CAST(SCOPE_IDENTITY() AS INT);` |

**MSSQL:** Append `SELECT CAST(SCOPE_IDENTITY() AS INT)` and call `ExecuteScalarAsync<int>`.
**Postgres:** Use `RETURNING id` on the INSERT and call `ExecuteScalarAsync<Guid>` (Postgres PK is `uuid`).
**MariaDB:** Append `SELECT LAST_INSERT_ID()` and call `ExecuteScalarAsync<int>`.

**Note:** The Postgres PK type is `uuid`, not `int` — the C# type and the response wire format (`"seq_no"`) will differ per engine. This is a non-trivial branching point. Consider a `CreateToDoResult` abstraction that normalises to `string` regardless of engine.

**ISqlDialect candidate:** Add `InsertAndReturnId(string insertSql)` that returns the dialect-correct form.

---

## 5. Date Casting — CONVERT(date, ...)

**Description:** Several endpoints compare a `datetime` column to a date parameter by casting the column to `date` using `CONVERT(date, column)`. This is MSSQL syntax. Postgres and MariaDB use `CAST(column AS DATE)` or MariaDB's `DATE(column)`.

| Source file | Method |
|---|---|
| `Endpoints/GetByTourSeriesDepartureEndpoint.cs` | `HandleAsync` — `CONVERT(date, DepDate) = @DepDate` |
| `Endpoints/ListByAssigneeEndpoint.cs` | `HandleAsync` — `CONVERT(date, t.DueDate) >= @DueDateFrom` |
| `Endpoints/ListByTourSeriesDepartureEndpoint.cs` | `HandleAsync` — `CONVERT(date, t.DepDate)` |
| `Endpoints/ListByClientEndpoint.cs` | `HandleAsync` — `CONVERT(date, t.DueDate)` |
| `Endpoints/ListBySupplierEndpoint.cs` | `HandleAsync` — `CONVERT(date, t.DueDate)` |
| `Endpoints/ListByTaskSourceEndpoint.cs` | `HandleAsync` — `CONVERT(date, t.DueDate)` |
| `Endpoints/SummaryByContextEndpoint.cs` | `HandleAsync` — `CONVERT(date, @DepDate)` |

**MSSQL:** `CONVERT(date, column)`
**Postgres:** `CAST(column AS DATE)` (column is `date` type natively — cast may not be needed at all)
**MariaDB:** `DATE(column)` or `CAST(column AS DATE)`

**ISqlDialect candidate:** Add `CastToDate(string columnExpression)`.

---

## 6. Pagination Clause — OFFSET FETCH vs LIMIT OFFSET ✅ RESOLVED

**Description:** All eight list endpoints use MSSQL's `OFFSET n ROWS FETCH NEXT m ROWS ONLY` syntax for pagination. Postgres and MariaDB use `LIMIT m OFFSET n`.

**Resolution:** Added `OffsetFetchClause(int skip, int take)` to `ISqlDialect` (pagination-only fragment — no ORDER BY prefix, safe for queries that carry their own ORDER BY). Implemented in all three dialects. All eight list endpoints now call `dialect.OffsetFetchClause(skip, fetch)`.

Note: `PaginationClause(skip, take)` still exists for queries that need a bundled `ORDER BY (SELECT NULL) OFFSET ... FETCH` (e.g. unordered queries). The new `OffsetFetchClause` is the correct method for ordered list queries.

---

## 7. Cross-Database Lookup JOINs (MSSQL only)

**Description:** All list endpoints JOIN to lookup tables in other MSSQL databases (`sales97.dbo.Priority`, `sales97.dbo.users`, `sales97.dbo.accountmast`) to return description fields alongside each row. Cross-database JOINs are a feature unique to MSSQL. Postgres and MariaDB do not support them; these lookup tables must be migrated into the service database or accessed via a separate lookup service call.

| Source file | Method | Lookup tables joined |
|---|---|---|
| `Endpoints/ListByAssigneeEndpoint.cs` | `HandleAsync` | `Priority`, `users` (×3), `accountmast` |
| `Endpoints/ListByTourSeriesDepartureEndpoint.cs` | `HandleAsync` | `Priority`, `users` (×3), `accountmast` |
| `Endpoints/ListByBookingEndpoint.cs` | `HandleAsync` | `Priority`, `users` (×3), `accountmast` |
| `Endpoints/ListByQuoteEndpoint.cs` | `HandleAsync` | `Priority`, `users` (×3), `accountmast` |
| `Endpoints/ListByCampaignEndpoint.cs` | `HandleAsync` | `Priority`, `users` (×3), `accountmast` |
| `Endpoints/ListByClientEndpoint.cs` | `HandleAsync` | `Priority`, `users` (×3), `accountmast` |
| `Endpoints/ListBySupplierEndpoint.cs` | `HandleAsync` | `Priority`, `users` (×3), `accountmast` (supplier type) |
| `Endpoints/ListByTaskSourceEndpoint.cs` | `HandleAsync` | `Priority`, `users` (×3), `accountmast` |

**MSSQL:** Inline `LEFT JOIN sales97.dbo.Priority p ON p.code = t.PriorityCode` (and equivalent for users and accountmast).
**Postgres / MariaDB:** Options — (a) replicate lookup tables into the service database and join locally; (b) drop the join and make a second query per page to a lookup API; (c) accept `null` for the description fields until lookups are migrated.

**Note:** The `TourCode` and `ItineraryName` columns are currently `NULL` placeholders in all list queries — they require additional joins to `fit.dbo.bookingdetail` (for bookings) and `fit.dbo.fitquote` (for quotes). These joins are also MSSQL cross-database and are not yet implemented. See the `/* TODO: JOIN fit.dbo.* */` comments in each list endpoint.

---

## 8. Cross-Database Lookup Validation Queries

**Description:** The Create and Update endpoints contain `// TODO` markers for lookup validation (422 responses). When implemented, these queries hit lookup tables across MSSQL databases. The same cross-database limitation from item 7 applies. Each validation query must be adapted per engine.

| Source file | Method | Fields to validate |
|---|---|---|
| `Endpoints/CreateToDoEndpoint.cs` | `HandleAsync` | `JobCode` → `sales97.dbo.jobs`; `AssignedToUserCode`, `AssignedByUserCode` → `sales97.dbo.users`; `PriorityCode` → `sales97.dbo.Priority`; `BkgNo` → `fit.dbo.bookingdetail`; `QuoteNo` → `fit.dbo.fitquote`; `CampaignCode` → `sales97.dbo.products`; `AccountCodeClient` → `sales97.dbo.accountmast`; `TourSeriesCode` → `brochure.dbo.brochure`; `DepDate` → `brochure.dbo.validdates`; `SupplierCode` → `sales97.dbo.accountmast`; `TravelPrnNo` → `fit.dbo.TravelPNRDeadline`; `SeqNoCharges` → `fit.dbo.charges`; `ItineraryNo` → `fit.dbo.bkgitinerary` |
| `Endpoints/UpdateToDoEndpoint.cs` | `HandleAsync` | Same set, minus task-source fields (immutable after insert) |

**MSSQL:** Direct cross-database `SELECT * FROM <db>.dbo.<table> WHERE code = @param`.
**Postgres / MariaDB:** Same strategy choices as item 7 — local replication, lookup service, or deferred validation.

---

## 9. FrzInd / DoneInd Column Type — Bit vs Boolean ✅ RESOLVED

**Description:** MSSQL uses `bit` (0 / 1) for boolean columns. Postgres uses `boolean` (`false` / `true`). Hardcoded `0`/`1` literals in dynamic SQL conditions would fail on Postgres.

**Resolution:** All endpoints now use `dialect.BooleanLiteral(bool)`:
- `t.frz_ind = {dialect.BooleanLiteral(false)}` replaces `t.FrzInd = 0`
- `t.done_ind = {dialect.BooleanLiteral(request.DoneInd.Value)}` replaces `t.DoneInd = {0 or 1}`
- Summary endpoints use `{doneOff}` / `{doneOn}` / `{frzOff}` variables resolved via `dialect.BooleanLiteral()`

`BooleanLiteral` produces `0`/`1` for MSSQL/MariaDB and `false`/`true` for Postgres. No further changes required for boolean literal conditions.

---

## 10. Summary — Completed Scope Queries (Client and Supplier)

**Description:** `SummaryByContextEndpoint` calculates "completed tasks" differently depending on context type. For `account_code_client`, the completed count should scope to open enquiries, open quotes, and bookings up to 15 days after return date. For `supplier_code`, scope is the last 30 days. Both are currently `NULL` / placeholder and must be implemented as inline sub-queries or CTEs, which will differ per DB engine.

| Source file | Method | Context |
|---|---|---|
| `Endpoints/SummaryByContextEndpoint.cs` | `BuildCompletedFilter` — `/* TODO: AND (inline scope for client ...) */` | `account_code_client` — join to open bookings/quotes, 15-day return window |
| `Endpoints/SummaryByContextEndpoint.cs` | `BuildCompletedFilter` — `AND DoneOn >= @SupplierWindowStart` | `supplier_code` — 30-day DoneOn window (this part is implemented; window start is config-driven) |

**MSSQL:** Inline sub-select joining `fit.dbo.bookingdetail` and `fit.dbo.fitquote` for client scope.
**Postgres / MariaDB:** Same logic without cross-DB joins — the booking / quote data must reside in the same database or be fetched separately.

**Config:** Window sizes are hot-reloadable from `opsettings.json → ToDoSummary`:
```json
"ToDoSummary": {
  "ClientCompletedWindowDays": 15,
  "SupplierCompletedWindowDays": 30
}
```

---

## 11. Column Name Casing — PascalCase vs snake_case

**Description:** MSSQL column names are PascalCase (`SeqNo`, `JobCode`, `Accountcode_Client`). Dapper maps them case-insensitively and strips underscores, so the mapping to C# property names works automatically. Postgres columns created by `Migrations/Postgres/V001__CreateToDo.sql` are snake_case (`seq_no`, `job_code`, `account_code_client`). Dapper works with snake_case too, but only if `DefaultTypeMap.MatchNamesWithUnderscores = true` is set globally, or column aliases are used in every SELECT.

| Source file | Method | Detail |
|---|---|---|
| `Models/ToDoRow.cs` | — | Property names match MSSQL PascalCase convention. For Postgres, either enable `DefaultTypeMap.MatchNamesWithUnderscores = true` in `Program.cs` before first use, or alias every selected column in the query. |
| `Models/ToDoListRow.cs` | — | Same as above |
| All `SELECT` queries | `HandleAsync` in every endpoint | Column aliases like `Accountcode_Client AS AccountcodeClient` may be needed for Postgres selects |

**Recommended fix:** Add `Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;` in `Program.cs` (before `app.Run()`). This is a one-line global setting and resolves the mapping for all Dapper queries in the service.

---

## Summary Table

| # | Issue | Engines affected | Status | Priority |
|---|---|---|---|---|
| 1 | Three-part table names | Postgres, MariaDB | ✅ Resolved — `dialect.TableRef()` used throughout | High |
| 2 | TOP 1 vs LIMIT 1 | Postgres, MariaDB | Open — add `dialect.SelectTop1()` or use `OffsetFetchClause(0,1)` | High |
| 3 | GETUTCDATE() | Postgres, MariaDB | Open — add `dialect.UtcNowFunction()` | High |
| 4 | SCOPE_IDENTITY() after INSERT | Postgres, MariaDB | Open — add `dialect.InsertAndReturnId()` | High |
| 5 | CONVERT(date, ...) | Postgres, MariaDB | Open — add `dialect.CastToDate()` | Medium |
| 6 | OFFSET FETCH vs LIMIT OFFSET | Postgres, MariaDB | ✅ Resolved — `dialect.OffsetFetchClause()` added and used | Medium |
| 7 | Cross-DB lookup JOINs | Postgres, MariaDB | Open — architectural decision required | High |
| 8 | Cross-DB validation queries | Postgres, MariaDB | Open — architectural decision required | High |
| 9 | FrzInd/DoneInd bit vs boolean | Postgres | ✅ Resolved — `dialect.BooleanLiteral()` used throughout | Medium |
| 10 | Summary completed scope queries | All (not yet written) | Open — incomplete placeholder | Medium |
| 11 | Column name casing for Dapper | Postgres | Open — add `DefaultTypeMap.MatchNamesWithUnderscores = true` in `Program.cs` | Low |
