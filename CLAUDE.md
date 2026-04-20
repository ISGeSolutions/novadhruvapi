# Nova Platform — Coding Conventions for AI

Read this file before writing any code or SQL for this project.

---

## Identifier casing — three layers

| Layer | Convention | Example |
|---|---|---|
| C# classes and properties | PascalCase | `TenantId`, `BkgNo`, `CreatedAt` |
| DB columns and table names (all new tables) | snake_case | `tenant_id`, `bkg_no`, `created_at` |
| JSON API — all request and response fields | snake_case | `"tenant_id"`, `"bkg_no"` |

**Legacy MSSQL tables and columns** use PascalCase (existing, inconsistent). See MSSQL-LEGACY rules below.

---

## Dapper mapping

`DefaultTypeMap.MatchNamesWithUnderscores = true` is set globally at startup in every service.

This maps snake_case DB columns to PascalCase C# properties automatically:
- `tenant_id` → `TenantId`
- `bkg_no` → `BkgNo`
- `created_at` → `CreatedAt`

**Do not** add SQL aliases for new-table columns to work around mapping — the global setting handles it.

**DTOs for tables with a legacy MSSQL counterpart** carry both PK fields:
```csharp
public Guid Id    { get; set; }  // populated for Postgres + MariaDB (uuid)
public int  SeqNo { get; set; }  // populated for legacy MSSQL (int identity)
```
All other columns are identical across dialects — legacy time columns aliased with `FORMAT(col, 'HH:mm')` to match the `varchar(5)` type in new tables.

---

## MSSQL-LEGACY queries

Any query that touches a legacy MSSQL table **must** carry this comment on the same line as or directly above the SQL string:

```
// MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by XXXX on dd MMM yyyy.
```

- Date = date the query was written or last modified.
- `Reviewed by` line is appended by the reviewer after team review.
- This comment is searchable: `grep -r "MSSQL-LEGACY"` gives the full audit list.
- **Nullable `bit` columns** (e.g. `FrzInd bit NULL`) must be coerced in the alias: `ISNULL(FrzInd, 0) AS frz_ind`. New tables use `boolean NOT NULL DEFAULT false` — the DTO property is non-nullable `bool`.

**Why:** Legacy columns are PascalCase and often inconsistent (`BookingNo`, `BkgNo`). All must be aliased to their canonical snake_case name so `MatchNamesWithUnderscores` maps correctly.

**Lookup:** `dev-tools/naming-registry.db` — see `dev-tools/naming-registry-queries.md` for queries.

---

## DB constraint and index naming

| Constraint | Pattern | Example |
|---|---|---|
| Primary key | `pk_{table_name}` | `pk_tenant_user_auth` |
| PK — all new tables | `id uuid` (UUID v7, app-generated before INSERT) | `Guid Id` |
| PK — legacy MSSQL tables | `SeqNo int IDENTITY` | `int SeqNo` |
| Unique | `uq_{table_name}_{col}` | `uq_tenant_user_profile_email` |
| Foreign key | `fk_{child_table}_{parent_table}` | `fk_booking_detail_booked_tours` |
| Index (1–2 cols) | `ix_{table_name}_{col1}_{col2}` | `ix_todo_bkg_no` |
| Index (3+ cols) | `ix_{table_name}_NN` (01, 02 …) | `ix_todo_01` |

NN is scoped per table. Check existing migrations for the same table before assigning the next NN.

---

## Date and time rules

Three categories. Use the correct type for each — **never mix them**.

### 1. Calendar date (`date` / `DateOnly`)

- **Stored as:** `date` (Postgres, MariaDB), `date` (MSSQL new), `datetime` (MSSQL legacy — store midnight, ignore time)
- **C# type:** `DateOnly`
- **JSON format:** `yyyy-MM-dd` (e.g. `"2026-04-14"`)
- **Rule:** No timezone, no time component. The browser does **not** adjust this value for locale.
- **Examples:** `due_date`, `start_date`, `dep_date`, `birth_date`

### 2. Time-of-day (`varchar(5)` / `string`)

- **Stored as:** `varchar(5)` in **all** dialects — never a DB time type.
- **C# type:** `string`
- **Format:** `HH:mm` (24-hour, e.g. `"14:30"`)
- **Rule:** No date, no timezone. Critical in travel domain — a departure time is an absolute clock value, not a UTC offset.
- **Examples:** `due_time`, `start_time`, `est_job_time`

### 3. UTC timestamp (`timestamptz` / `DateTimeOffset`)

- **Stored as:** `timestamptz` (Postgres), `datetime(6)` (MariaDB), `datetime2` (MSSQL new), `datetime` (MSSQL legacy)
- **C# type:** `DateTimeOffset` (new tables). `DateTime` (MSSQL legacy — projection layer converts)
- **JSON format:** `yyyy-MM-ddTHH:mm:ssZ` (always UTC, always Z suffix, e.g. `"2026-04-14T09:30:00Z"`)
- **Rule:** The browser **will** adjust this value for the user's locale when displaying. UX always sends UTC in request bodies — never local time.
- **Examples:** `created_on`, `updated_on`, `expires_on`, `done_on`, `processed_at`

### Legacy MSSQL datetime rule

ALL columns in legacy MSSQL tables are `datetime` (not `datetime2`, not `datetimeoffset`).
DTOs for legacy MSSQL queries use `DateTime`, not `DateTimeOffset` or `DateOnly`.
The projection layer converts to the canonical C# type before returning to the caller.
See `docs/datetime-design.md` for the full design.

---

## Naming registry

Before naming any new column or table, consult:

```
dev-tools/naming-registry.db
dev-tools/naming-registry-queries.md   ← query patterns
dev-tools/naming-registry.sql          ← source of truth (edit this, regenerate .db)
```

The registry contains:
- All approved column names with descriptions (`nova_db_column`)
- All tables with short aliases (`nova_db_table`)
- Legacy MSSQL alias mappings (`nova_db_column_legacy`)

---

## Architecture constraints (enforced — do not override)

- No EF Core. Dapper + explicit SQL only.
- No MediatR. No AutoMapper.
- No interface-for-everything. Only add interfaces where genuinely needed.
- Lean Clean Architecture (Ardalis-inspired).
- English UK identifiers throughout.
- One database per tenant — no shared DB with `tenant_id` row filtering.
- Mono-repo with project references (not NuGet) during active development.
