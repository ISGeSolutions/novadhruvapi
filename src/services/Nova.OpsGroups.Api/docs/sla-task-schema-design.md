# SLA Task Schema Design — Team Review

**Status:** Draft v4 — UX API contract questions answered; `sla_task_exclusion` dropped, `kind` column added to `sla_task`.  
**Author:** Rajeev Jha  
**Date:** 2026-04-25

---

## Overview

The SLA grid stores offset days per cell across four scope levels: Global, Tour Generic, Tour Series, and Tour Departure. Each cell is the intersection of an **enquiry event code** (DP, RT, JI) and a **task code** (CQ, CC, PV, TR, FR, FT, HB, HR, LG, TL, LI, RL, JI, IF, AP, FC, RN, PL).

Design goals:
- Normalised rows (one row per cell with an explicit state) — no wide tables with 18 nullable int columns
- Three explicit cell states: **Set** (explicit offset), **N/A** (not applicable), **Inherit** (no override — resolved from parent scope)
- Full audit trail of every state transition (who, when, old state → new state)
- UUID-based scope references for stability (code renames don't orphan SLA data)
- All new tables in the `presets` database (MSSQL / MariaDB) / `presets` schema (Postgres)

---

## Cell State Model

Every grid cell is in one of three states:

| State | Stored in DB | Meaning |
|-------|-------------|---------|
| **Set** | Row in `sla_task` with `kind = 'SET'` and `offset_days = n` | Explicit offset at this scope |
| **N/A** | Row in `sla_task` with `kind = 'NA'` and `offset_days = NULL` | Task does not apply at this scope for this lifecycle event |
| **Inherit** | No row in `sla_task` | No override — resolved from the nearest ancestor scope that has a row |

`inherit` at Global scope resolves to nothing — treat as `na` when rendering.

**N/A is per-cell** (`enq_event_code` + `task_code`), not per task. A task can be N/A for Departure but still Set for Return at the same scope level.

**N/A is terminal in the inheritance chain.** If resolution walks up and hits an `na` row, descendants that say `inherit` see `na`. A descendant scope can override `na` back to `set` explicitly.

---

## UX Hierarchy Behaviour

When a user selects **Global**: only the GLOB grid is shown (editable).

When a user selects a **Tour Generic** (e.g. BHU):
- GLOB grid shown as **read-only** (inherited baseline)
- BHU (TG) grid shown as **editable**

When a user drills into a **Tour Series** (e.g. BHU2026):
- GLOB + BHU consolidated view shown as **read-only**
- BHU2026 (TS) grid shown as **editable**

When a user drills into a **Tour Departure** (e.g. BHU2026, 15 Oct 2026):
- GLOB + BHU + BHU2026 consolidated view shown as **read-only**
- That departure (TD) grid shown as **editable**

Only **future** tour series (any series with at least one departure date after today) are loaded.

---

## Scope Hierarchy

| Level | scope_type | scope_id source | Notes |
|-------|-----------|----------------|-------|
| Global | `'GLOB'` | `presets.tour_generics.id` where `tour_generic_code = 'GLOBAL'` | Special tour_generic row |
| Tour Generic | `'TG'` | `presets.tour_generics.id` | Existing table |
| Tour Series | `'TS'` | `presets.tour_series.id` | New table — V005 |
| Tour Departure | `'TD'` | `presets.tour_departures.id` | Renamed from `grouptour_departures` |

`scope_id` is a UUID across all four levels. Polymorphic column — DB-level FK not possible; referential integrity enforced at application layer.

`scope_type` enforced by CHECK constraint: `scope_type IN ('GLOB','TG','TS','TD')`.

---

## Table: `presets.tour_generics` — Amended Columns

Two columns added by **overwriting the existing V004 migration** (tables not yet live, drop and recreate):

| Column | Type | Nullable |
|--------|------|----------|
| `company_code` | `varchar(4)` | NO |
| `branch_code` | `varchar(4)` | NO |

---

## New Lookup Table: `presets.enquiry_events`

**Migration:** `Nova.Presets.Api/Migrations/{Dialect}/V005__AddTourSeriesAndSlaTask.sql`

| Column | Type | Nullable | Notes |
|--------|------|----------|-------|
| `enq_event_code` | `varchar(5)` | NO | PK |
| `enq_event_name` | `varchar(100)` | NO | |
| `sort_order` | `int` | NO | |

**Seed data:**

| enq_event_code | enq_event_name | sort_order |
|---------------|---------------|-----------|
| `DP` | Departure Date | 1 |
| `RT` | Return Date | 2 |
| `JI` | JI Date | 3 |

**Constraints:** `pk_enquiry_events` — PRIMARY KEY (`enq_event_code`)

---

## New Catalog Table: `presets.tour_series`

**Migration:** same V005 file.

| Column | Type | Nullable | Notes |
|--------|------|----------|-------|
| `id` | `uuid` | NO | PK — UUID v7, app-generated |
| `tenant_id` | `varchar(10)` | NO | |
| `tour_generic_id` | `uuid` | NO | FK → `presets.tour_generics(id)` |
| `tour_series_code` | `varchar(10)` | NO | Unique per tenant |
| `tour_series_name` | `varchar(200)` | NO | |
| `frz_ind` | `boolean` | NO | DEFAULT false |
| `created_by` | `varchar(10)` | NO | |
| `created_on` | `timestamptz` | NO | |
| `updated_by` | `varchar(10)` | NO | |
| `updated_on` | `timestamptz` | NO | |
| `updated_at` | `varchar(50)` | NO | Service identifier |

No denormalised columns. Join to `tour_generics` when `tour_generic_code` is needed.

**Constraints:**
- `pk_tour_series` — PRIMARY KEY (`id`)
- `uq_tour_series_tour_series_code` — UNIQUE (`tenant_id`, `tour_series_code`)
- `fk_tour_series_tour_generics` — FOREIGN KEY (`tour_generic_id`) REFERENCES `presets.tour_generics(id)`

Additional columns via follow-up migration when required.

---

## Renamed Table: `presets.tour_departures`

Currently `opsgroups.grouptour_departures`. Overwrite OpsGroups V001 migration — rename to `tour_departures` and move to `presets` schema.

---

## New Table: `presets.sla_task`

One row per cell with an explicit state (Set or N/A). Cells in the `inherit` state have no row.

**Migration:** same V005 file.

| Column | Type | Nullable | Notes |
|--------|------|----------|-------|
| `id` | `uuid` | NO | PK — UUID v7, app-generated |
| `tenant_id` | `varchar(10)` | NO | |
| `scope_type` | `varchar(4)` | NO | `'GLOB'` \| `'TG'` \| `'TS'` \| `'TD'` |
| `scope_id` | `uuid` | NO | ID from respective scope table |
| `enq_event_code` | `varchar(5)` | NO | FK → `presets.enquiry_events` |
| `task_code` | `varchar(10)` | NO | `'CQ'` \| `'CC'` \| `'PV'` … |
| `kind` | `varchar(5)` | NO | `'SET'` \| `'NA'` |
| `offset_days` | `int` | YES | Required when `kind = 'SET'`; NULL when `kind = 'NA'` |
| `updated_by` | `varchar(10)` | NO | |
| `updated_on` | `timestamptz` | NO | |

**Constraints:**
- `pk_sla_task` — PRIMARY KEY (`id`)
- `uq_sla_task_scope_event_task` — UNIQUE (`tenant_id`, `scope_type`, `scope_id`, `enq_event_code`, `task_code`)
- `ix_sla_task_scope` — INDEX (`tenant_id`, `scope_type`, `scope_id`)
- CHECK: `scope_type IN ('GLOB','TG','TS','TD')`
- CHECK: `kind IN ('SET','NA')`
- CHECK: `(kind = 'SET' AND offset_days IS NOT NULL) OR (kind = 'NA' AND offset_days IS NULL)`
- FK: `enq_event_code` → `presets.enquiry_events(enq_event_code)`

> UX label for `enq_event_code` remains "Reference from" — display-only.

---

## New Table: `presets.sla_task_audit`

One row per state transition. Covers all three kinds: Set ↔ Set, Set ↔ N/A, Set/N/A → Inherit (row deleted), Inherit → Set/N/A (row created).

**Migration:** same V005 file.

| Column | Type | Nullable | Notes |
|--------|------|----------|-------|
| `id` | `uuid` | NO | PK — UUID v7, app-generated |
| `tenant_id` | `varchar(10)` | NO | |
| `scope_type` | `varchar(4)` | NO | |
| `scope_id` | `uuid` | NO | |
| `enq_event_code` | `varchar(5)` | NO | |
| `task_code` | `varchar(10)` | NO | |
| `kind_old` | `varchar(5)` | YES | NULL = prior state was Inherit (no row existed) |
| `offset_days_old` | `int` | YES | |
| `kind_new` | `varchar(5)` | YES | NULL = new state is Inherit (row deleted) |
| `offset_days_new` | `int` | YES | |
| `changed_by` | `varchar(10)` | NO | |
| `changed_on` | `timestamptz` | NO | |

**Constraints:**
- `pk_sla_task_audit` — PRIMARY KEY (`id`)
- `ix_sla_task_audit_scope` — INDEX (`tenant_id`, `scope_type`, `scope_id`, `changed_on`)

---

## Wire Format (API Contract)

### Cell shape — Option A (tagged object) — same for read and save

```json
{ "kind": "set",     "offset_days": -30 }   // explicit offset
{ "kind": "na"                           }   // not applicable at this scope
{ "kind": "inherit"                      }   // no override — walk up the chain
```

- Option B (sentinel string + number) rejected: mixed types in the same JSON field breaks deserializers.
- Option C (two parallel fields) rejected: allows contradictory states (`offset_days: 30, applicability: "not_applicable"`).
- `kind` and `offset_days` are snake_case per Nova JSON convention. UX uses `offsetDays` in its TypeScript types — the API layer translates.
- The same shape is used on both read (`fetchSLAHierarchy`) and save (`saveGroupTaskSLARuleV2`). What you receive is what you send back with changes.

### Save diff payload

```json
{
  "group_task_code": "CQ",
  "enq_event_code": "DP",
  "old": { "kind": "set", "offset_days": 30 },
  "new": { "kind": "na" }
}
```

- `inherit` is a first-class value in the diff. When a user removes an override, emit: `{ "old": { "kind": "set", "offset_days": 30 }, "new": { "kind": "inherit" } }`. The API deletes the row and writes an audit entry.
- The server accepts `new: inherit` unconditionally and lets the resolver return `na` if all ancestors are also Inherit or N/A. The API does not validate the resolved ancestor chain at save time.

---

## Global Scope Rules

- `inherit` sent to Global scope is rejected with **400**. No silent coercion.
- `na` is allowed at Global. A global `na` means "this lifecycle event is never tracked for any tour, ever."
- A global `na` is **not terminal if explicitly overridden** — a child scope can set `kind = 'SET'` to re-enable the task at that scope. However, a child scope that says `inherit` will resolve through and see `na`.

---

## Inheritance Resolution (Server-Side)

The server resolves the full ancestor chain and returns both the scope's own state and the resolved effective value with provenance. Client does not implement resolution logic.

**Per-cell response shape:**

```json
{
  "enq_event_code": "DP",
  "task_code":      "CQ",
  "own":            { "kind": "inherit" },
  "resolved":       { "kind": "set", "offset_days": -20 },
  "resolved_from":  "TG"
}
```

| Field | Meaning |
|-------|---------|
| `own` | What this scope explicitly holds — `set`, `na`, or `inherit` (no row) |
| `resolved` | Effective value after walking the ancestor chain — always `set` or `na`, never `inherit` |
| `resolved_from` | Which scope level the resolved value came from: `'GLOB'`, `'TG'`, `'TS'`, `'TD'` |

**Resolution algorithm (server executes for each cell):**
1. Walk TD → TS → TG → GLOB. Take the first scope that has a row for this (`enq_event_code`, `task_code`).
2. If that row is `kind = 'SET'` → `resolved = set, offset_days = n`.
3. If that row is `kind = 'NA'` → `resolved = na`. No further walking (na is terminal).
4. If no row found at any level → `resolved = na`, `resolved_from = 'GLOB'` (Global implicit na).

The UX uses `resolved` to render the effective cell value and `resolved_from` to display provenance (e.g. ghosted "−20 from TG").

**Version field:** Whole-scope version, bumped on any cell change within that scope. Per-cell versioning is not required.

---

## Downstream Effects

| Event | Behaviour |
|-------|-----------|
| Cell flips to `na` (resolved) | Task generator does **not** create a group task for this departure/lifecycle event |
| Cell flips to `na` after tasks already generated | Soft-delete existing tasks: set `frz_ind = true`. Can be reversed if cell is later re-set. |
| Readiness calculation | `na` cells excluded from both numerator and denominator — mirrors existing `not_applicable` task status |
| Socket events | `task-counts-updated` and `sla-rule-updated` fire on `na` changes, same as numeric changes |

---

---

## Write Logic

### Setting a cell (kind = SET or NA)

1. UX sends `scope_type` + `scope_code` + `enq_event_code` + `task_code` + `kind` + `offset_days`.
2. API resolves `scope_id` via single lookup on the appropriate table.
3. UPSERT `sla_task` on unique key — capture old `kind`/`offset_days` before upsert.
4. INSERT `sla_task_audit` with `kind_old`/`offset_days_old` (NULLs if no prior row), `kind_new`/`offset_days_new`.

### Clearing a cell (kind = INHERIT — user removes override)

1. DELETE from `sla_task` matching the unique key — capture old row first.
2. INSERT `sla_task_audit` with `kind_old`/`offset_days_old` from deleted row, `kind_new = NULL`, `offset_days_new = NULL`.

---

## Fetch Logic

```sql
SELECT * FROM presets.sla_task
WHERE tenant_id = ? AND scope_type = ? AND scope_id = ?
```

Maximum 54 rows (18 task codes × 3 event codes). Pivot to grid in C#.

For the consolidated read-only ancestor view: fetch all ancestor levels, merge top-down. Lower scope overrides higher where a row exists. Resolution stops at the first row found for each cell; `na` terminates further resolution for that cell.

---

## Files to Create / Modify

| File | Action |
|------|--------|
| `Nova.Presets.Api/Migrations/Postgres/V004__AddGroupTasksAndTourGenerics.sql` | Overwrite — add `company_code`, `branch_code` to `tour_generics` |
| `Nova.Presets.Api/Migrations/MsSql/V004__AddGroupTasksAndTourGenerics.sql` | Overwrite — same |
| `Nova.Presets.Api/Migrations/MariaDb/V004__AddGroupTasksAndTourGenerics.sql` | Overwrite — same |
| `Nova.Presets.Api/Migrations/Postgres/V005__AddTourSeriesAndSlaTask.sql` | Create |
| `Nova.Presets.Api/Migrations/MsSql/V005__AddTourSeriesAndSlaTask.sql` | Create |
| `Nova.Presets.Api/Migrations/MariaDb/V005__AddTourSeriesAndSlaTask.sql` | Create |
| `Nova.OpsGroups.Api/Migrations/*/V001__CreateOpsGroups.sql` | Overwrite — rename `grouptour_departures` → `tour_departures`, move to `presets` schema |
| `Nova.OpsGroups.Api/Endpoints/SlaRules/SlaHierarchyEndpoint.cs` | Rewrite — four-level design, new wire format |
| `dev-tools/naming-registry.sql` | Add new tables + columns |

---

## Design Decisions — Closed

| # | Question | Decision |
|---|----------|---------|
| 1 | Additional columns on `tour_series` now? | No — follow-up migration when needed |
| 2 | `offset_days` type? | `int` |
| 3 | Four-level vs two-level `SlaHierarchyEndpoint`? | Replace entirely — four-level was always correct |
| 4 | `enq_event_code` enforcement? | FK to `presets.enquiry_events` |
| 5 | `scope_type` enforcement? | CHECK constraint |
| 6 | `nvarchar` vs `varchar`? | `varchar` — Nova convention; UTF-8 MSSQL collation |
| 7 | N/A task exclusion mechanism? | `kind` column in `sla_task` (`'SET'`\|`'NA'`); absence of row = Inherit. `sla_task_exclusion` table dropped. |
| Q1.1/1.2 | Wire cell shape? | Option A tagged object — same shape for read and save |
| Q2.1 | `inherit` at Global? | Rejected 400 |
| Q2.2 | `na` at Global allowed? | Yes |
| Q2.3 | Global `na` overridable by child? | Yes — child can explicitly set `kind = 'SET'` |
| Q3.1/3.2 | Diff payload shape? | Tagged objects; `inherit` is first-class in diff |
| Q3.3 | Server rejects `new: inherit` if chain produces nothing? | No — accept and let resolver return `na` |
| Q3.4 | `na ↔ inherit` auditable? | Yes — `sla_task_audit` via `kind_old`/`kind_new` |
| Q4.1/4.2 | Server resolves or client resolves? | Server resolves — returns `own` + `resolved` + `resolved_from` per cell |
| Q4.3 | `na` terminal for descendant inheritance? | Yes |
| Q4.4 | Version field per-cell or per-scope? | Per-scope |
| Q5.1 | Task generation treats resolved `na` as skip? | Yes |
| Q5.2 | Existing tasks when cell flips to `na`? | Soft-delete (`frz_ind = true`) |
| Q5.3 | Readiness excludes `na` from numerator and denominator? | Yes |
| Q5.4 | Socket events on `na` change? | Yes — same events as numeric changes |
| Q6 | Backwards compatibility / migration | Not applicable — project has no live users |
