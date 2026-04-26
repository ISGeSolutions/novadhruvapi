# Concurrency — Field-Group Versioning

**Decision date:** April 2026  
**Status:** Confirmed  
**Applies to:** All Nova services — any table with multiple independent update paths  
**Shared helper:** `Nova.Shared/Data/ConcurrencyHelper.cs`

---

## The Rule

> Optimistic concurrency lock tokens are scoped to logical update domains, not to the row.
> A conflict in one domain must never block an update in another domain.

A single row-level `lock_ver` column is too coarse when different parts of a row are owned by different
actors or processes. Use one `int` lock token column per logical update domain instead.

---

## When Does This Apply?

Apply field-group versioning when a table has columns that are updated independently by:
- Different UX forms or pages
- Different services or processes (e.g. user action vs. automated invoice processor)
- Different user roles (e.g. sales vs. accounts)

If a table is only ever updated from a single place, a single `lock_ver` column is sufficient.

---

## How to Identify the Groups

For each table, ask: **"Can two different people or processes update this row at the same time
without touching the same columns?"** Each independent answer is a version group.

Use these questions to draw the boundaries:

1. Which columns are updated together in a single form submit or API call?
2. Are any updates triggered by automated processes (invoicing, payment, import)?
3. Which columns are set once on creation and never changed after?

Fields set once on creation (e.g. `client_id`, `created_on`) need no version group — they are immutable after INSERT.

---

## Column Convention

### Why `lock_ver` not `version`

Business-versioning columns already exist in the domain — for example, `VersionNo` on `Tour_CostVersion`
represents a cost-sheet revision number, not a concurrency token. Using a plain `version` column would
cause ambiguity for any dev reading the schema. `lock_ver` immediately signals "optimistic locking"
and cannot be confused with a business concept.

### Row-level versioning (single domain — all columns change together)

Use one column named `lock_ver`:

```sql
lock_ver  int NOT NULL DEFAULT 0
```

### Field-group versioning (multiple independent domains)

Use one `lock_ver_{domain}` column per group:

```sql
-- Postgres / MariaDB / MSSQL (syntax identical across all three dialects)
lock_ver_status     int NOT NULL DEFAULT 0,
lock_ver_booking    int NOT NULL DEFAULT 0,
lock_ver_financials int NOT NULL DEFAULT 0
```

The `lock_ver_*` prefix groups all concurrency columns together in IDE IntelliSense — typing `lock_ver`
surfaces every concurrency column for the table at once.

**Starting value is 0.** `ConcurrencyHelper.NextVersion()` increments from 0 → 1 on the first update
and wraps from `int.MaxValue` back to 1 (never 0 again after the first write).

**Naming rule:** `lock_ver_{domain}` — the `lock_ver` prefix groups all lock columns together in IDE IntelliSense and tooling.
Common names: `lock_ver_status`, `lock_ver_booking`, `lock_ver_financials`, `lock_ver_address`, `lock_ver_notes`.

---

## Example — BookingDetail

```
Columns                                      → Lock token column
─────────────────────────────────────────    ────────────────────
Status, StatusUpdatedBy, StatusUpdatedOn     → lock_ver_status
BookingValue, ReceiptValue                   → lock_ver_financials
HolidayType, AgentCode, SalesUserId,
  UpdatedBy, UpdatedOn                       → lock_ver_booking
ClientId                                     → (immutable — no lock token)
```

The migration adds three columns:

```sql
ALTER TABLE booking_detail ADD lock_ver_status     int NOT NULL DEFAULT 0;
ALTER TABLE booking_detail ADD lock_ver_financials int NOT NULL DEFAULT 0;
ALTER TABLE booking_detail ADD lock_ver_booking    int NOT NULL DEFAULT 0;
```

---

## How Each Update Path Uses It

### 1. Read — return the relevant lock token to the client

Each endpoint response includes only the lock token for its domain.
The status form does not need to know `lock_ver_financials`.

```json
// GET /booking/{id}/status-form
{
  "status":           "confirmed",
  "lock_ver_status":  3
}

// GET /booking/{id}
{
  "holiday_type":     "group",
  "sales_user_id":    "USR042",
  "lock_ver_booking": 7
}
```

### 2. Write — check and increment only the domain's lock token

The SQL for each update path checks only its own lock token column:

```sql
-- Status update
UPDATE booking_detail
SET    status              = @Status,
       status_updated_by   = @UpdatedBy,
       status_updated_on   = @UpdatedOn,
       lock_ver_status     = @NextVersion
WHERE  id                  = @Id
AND    lock_ver_status     = @ExpectedVersion
```

```sql
-- Financials update (invoice / payment processor)
UPDATE booking_detail
SET    booking_value        = @BookingValue,
       receipt_value        = @ReceiptValue,
       lock_ver_financials  = @NextVersion
WHERE  id                   = @Id
AND    lock_ver_financials  = @ExpectedVersion
```

```sql
-- Main booking page save
UPDATE booking_detail
SET    holiday_type         = @HolidayType,
       agent_code           = @AgentCode,
       sales_user_id        = @SalesUserId,
       updated_by           = @UpdatedBy,
       updated_on           = @UpdatedOn,
       lock_ver_booking     = @NextVersion
WHERE  id                   = @Id
AND    lock_ver_booking     = @ExpectedVersion
```

A salesperson changing `holiday_type` increments `lock_ver_booking` only. The payment processor
checks `lock_ver_financials`, which was never touched — no false conflict.

---

## Using the Shared Helper

`ConcurrencyHelper` lives in `Nova.Shared.Data`. Two methods:

| Method | Purpose |
|---|---|
| `ExecuteWithConcurrencyCheckAsync(connection, sql, parameters, transaction?)` | Executes the UPDATE; returns `true` if 1 row affected, `false` if 0 (conflict) |
| `NextVersion(int current)` | Computes the next lock token value; wraps safely at `int.MaxValue` |

```csharp
int nextVer = ConcurrencyHelper.NextVersion(req.ExpectedVersion);

bool success = await ConcurrencyHelper.ExecuteWithConcurrencyCheckAsync(
    db,
    BookingStatusUpdateSql,
    new
    {
        req.Id,
        req.Status,
        UpdatedBy       = req.UserId,
        UpdatedOn       = DateTimeOffset.UtcNow,
        NextVersion     = nextVer,
        ExpectedVersion = req.LockVerStatus
    });

if (!success)
    return await SendAsync(new ProblemDetails { Status = 409, Title = "Conflict",
        Detail = "Booking status was updated by someone else. Please reload." }, 409);
```

The helper enforces the discipline of checking rows affected — it does not write the SQL.
The SQL is always written by the caller so it is visible, reviewable, and dialect-independent.

---

## Automated Processes — Last-Write-Wins Consideration

For update paths that are **automated and non-interactive** (invoice processor, payment webhook,
data import), consider whether optimistic concurrency is the right choice at all:

- If the process is **idempotent and serialised** (e.g. a queue, one message at a time):
  a lock token check adds safety but a conflict would indicate a bug, not a user race.
  Keep the check — it catches double-processing.

- If the process **can race with itself** (two payment notifications arriving simultaneously):
  the lock token check is essential and the conflict should trigger a retry or dead-letter, not a 409.

- If the process **cannot race** and correctness does not depend on the previous value:
  last-write-wins (`UPDATE ... WHERE id = @Id` with no lock token check) is simpler and correct.

Document which approach applies per process in the service's SQL review doc.

---

## Known Limitation — Legacy MSSQL Dual-Write Window

When a `lock_ver` column is added to a legacy MSSQL table via ALTER TABLE, legacy code updating the same rows will **not** increment `lock_ver`. This means:

- **Nova-vs-Nova** conflicts: fully detected — both writers increment `lock_ver`.
- **Nova-vs-legacy** conflicts: undetected — legacy writer leaves `lock_ver` unchanged; Nova's WHERE clause matches and overwrites silently.

A combined check (`lock_ver` + `updated_on`) could partially close this gap but was explicitly rejected (April 2026) — it adds two concurrency tokens per endpoint, two validation paths, and doubles the conflict test cases on every legacy table. The complexity is not justified for a temporary dual-write window.

The gap closes automatically when a tenant migrates fully to Nova and legacy writes stop.

---

## Alternatives Considered

| Alternative | Why rejected |
|---|---|
| Single row-level `lock_ver` (for field-group scenarios) | Too coarse — any update in any domain blocks all other domains. Use `lock_ver_{domain}` columns instead |
| `version` / `version_{domain}` | Ambiguous with business version columns (e.g. `VersionNo` on `Tour_CostVersion`). Rejected April 2026 |
| JSON column `{ "status": 1, "booking": 3 }` | Cannot index; awkward SQL; no benefit over separate columns |
| Separate version-tracking table | Requires a JOIN on every read; two-row write on every update; overkill |
| Database row locking (`SELECT FOR UPDATE`) | Pessimistic — holds a lock for the duration of the user's think time; unacceptable for web UX |

Separate `int` columns are the simplest approach that maps directly to the logical update domains.
Storage cost is negligible: 4 bytes per lock token column, typically 2–4 columns per table.
