# SLA Hierarchy — Concurrent Edit Conflict Analysis

## The problem

The `PATCH /api/v1/group-task-sla-rule-save` endpoint accepts a batch of cell changes:

```json
{
  "scope_type": "GLOB",
  "changes": [
    {
      "enq_event_code": "DP",
      "task_code":      "CQ",
      "old_cell":       { "kind": "inherit" },
      "new_cell":       { "kind": "set", "offset_days": -30 }
    }
  ]
}
```

If User A and User B both open the Global SLA grid, then both edit `CQ / Departure` and both hit Save:

- User A's request lands first — CQ/DP is written as -30.
- User B's request lands second — CQ/DP is overwritten as -45.
- User A's save is silently discarded. No error. No notification. The audit log records both writes, but the live grid shows only User B's value.

This is a **last-write-wins silent overwrite**. The problem is most likely at the GLOB and TG scopes, which are shared across all users with edit rights.

---

## What is already in place

### Wire format — OldCell is already there

`old_cell` is already part of every `RuleSaveChange` in the request. The UX sends the value the user saw when they opened the grid. The contract was designed for conflict detection. **No API shape change is needed.**

### Server — reads current row but ignores OldCell

In `SlaHierarchyEndpoint.HandleRuleSaveAsync` (`Endpoints/SlaRules/SlaHierarchyEndpoint.cs`, inside the `foreach` loop):

```csharp
// Read current row (for audit old values)
SlaTaskRow? current = await conn.QueryFirstOrDefaultAsync<SlaTaskRow>(...);

string? kindOld   = current?.Kind;
int?    offsetOld = current?.OffsetDays;
// ↑ used only for audit INSERT below — never compared to change.OldCell
```

The server reads the current DB row to populate the audit trail (`kind_old`, `offset_days_old`), but `change.OldCell` is never used. The write proceeds unconditionally.

### Schema — sla_task_audit already captures full history

The audit table (`presets.sla_task_audit`) records every write with `kind_old`, `offset_days_old`, `kind_new`, `offset_days_new`, `changed_by`, `changed_on`. After a silent overwrite, the audit log would show both users' saves. But neither user is told about the collision at save time.

---

## What needs to change

### 1. Server — add conflict check (≈15 lines, no schema change)

After reading `current`, compare it to `change.OldCell` before writing. Collect all mismatches, roll back, and return 409 with a `conflict_cells` array.

**Current loop (simplified):**
```csharp
foreach (var change in request.Changes)
{
    var current = await conn.QueryFirstOrDefaultAsync<SlaTaskRow>(...);
    // audit vars set from current
    // write proceeds unconditionally
}
```

**With conflict detection:**
```csharp
var conflicts = new List<ConflictCell>();

foreach (var change in request.Changes)
{
    var current = await conn.QueryFirstOrDefaultAsync<SlaTaskRow>(...);

    // Map DB row → CellDto for comparison
    CellDto dbCell = current is null
        ? CellDto.Inherit
        : current.Kind == "SET" ? CellDto.Set(current.OffsetDays!.Value) : CellDto.Na;

    if (!CellsMatch(dbCell, change.OldCell))
    {
        conflicts.Add(new ConflictCell(change.EnqEventCode, change.TaskCode,
                                       change.OldCell, dbCell));
    }
}

if (conflicts.Count > 0)
{
    tx.Rollback();
    return TypedResults.Problem(
        title:      "Conflict",
        detail:     $"{conflicts.Count} cell(s) were modified by another user since you opened the grid.",
        statusCode: 409,
        extensions: new Dictionary<string, object?> { ["conflict_cells"] = conflicts });
}

// proceed with writes as before
```

`CellsMatch` is a one-line value comparison. `ConflictCell` is a small record.

**This change is entirely server-side. No migration. No API shape change for the happy path. The 409 body is new.**

### 2. API — 409 response shape (new, for UX to handle)

```json
{
  "status": 409,
  "title": "Conflict",
  "detail": "2 cell(s) were modified by another user since you opened the grid.",
  "conflict_cells": [
    {
      "enq_event_code": "DP",
      "task_code":      "CQ",
      "your_old_cell":  { "kind": "inherit" },
      "current_cell":   { "kind": "set", "offset_days": -45 }
    }
  ]
}
```

`your_old_cell` = what the user saw when they opened the grid (from `change.OldCell`).  
`current_cell` = what is in the DB right now (from the server read).

### 3. Schema — no change required

No new columns or tables are needed for cell-level conflict detection. The existing `sla_task` unique constraint on `(tenant_id, scope_type, scope_id, enq_event_code, task_code)` means concurrent upserts for the same cell are serialised by the transaction; the check against `OldCell` happens inside the transaction before any write.

---

## UX team requirements

### Happy path (no conflict)
No change. The save succeeds silently as today.

### Conflict path (409)
When the API returns 409 with `conflict_cells`:

1. **Do not apply the save.** Show a conflict banner: *"Someone else changed this grid while you were editing."*

2. **Highlight conflicted cells** in the grid. The `conflict_cells` array identifies each one by `enq_event_code` + `task_code`. Suggested visual: amber border on the cell, tooltip showing current DB value.

3. **Offer two choices:**
   - **Discard my changes** — re-fetch the grid (`POST /group-task-sla-hierarchy`) and discard the local edits. Simple.
   - **Overwrite anyway** — re-submit the same changes but populate `old_cell` from the current DB values returned in `conflict_cells[n].current_cell`. This tells the server "I know what's there, replace it." This is a force-save path; the UX should confirm before doing it.

4. **Do not implement a three-way merge UI** for the initial release. The grid is small (≤18 tasks × 3 events) and conflicts will be rare. Discard + re-edit is the correct default.

### What UX does NOT need to implement
- No polling / live lock indicators.
- No "user X is editing" presence markers.
- No per-cell version numbers in the grid UI.

---

## Complexity summary

| Layer | Work | Effort |
|---|---|---|
| DB schema | None | — |
| Server (`HandleRuleSaveAsync`) | Compare `OldCell` vs DB row in loop; collect conflicts; rollback + return 409 | ~15 lines |
| API contract | 409 response body shape (new) — document for UX | — |
| UX | Detect 409, highlight cells, offer Discard / Overwrite | Medium — 1–2 days |

Total backend effort: **2–3 hours** including tests.  
Total frontend effort: **1–2 days** depending on the conflict UI design.

---

## Recommendation — do now, not next release

**Do the server change now, before any real user touches the SLA grid.**

Reasons:

1. **Effort is trivial on the backend.** Fifteen lines inside an existing loop. The transaction and the DB read are already there. The wire format already carries `OldCell`.

2. **Silent data loss is worse than any other class of bug.** If User A's -30 offset is overwritten by User B's -45 with no indication, the first discovered symptom will be a task generated on the wrong date. That is a runtime correctness issue, not a UX inconvenience.

3. **The global scope is the highest-risk target.** GLOB SLA rules are shared across all tour generics and departures in the tenant. A single silent overwrite there propagates to every departure. Any tenant with more than one ops manager can hit this on day one.

4. **Fixing it later is harder.** Once users have adopted the workflow and the first silent overwrite occurs, trust in the SLA grid is damaged. Adding conflict detection after the fact does not recover lost data.

The UX conflict UI (highlight cells, Discard / Overwrite) can follow in the next sprint — the server returning 409 instead of silently overwriting is the urgent part. Until the UX is ready, a generic "Someone else changed the grid, please refresh" toast on 409 is sufficient.

---

## Files to change

| File | Change |
|---|---|
| `src/services/Nova.OpsGroups.Api/Endpoints/SlaRules/SlaHierarchyEndpoint.cs` | Add conflict loop before writes in `HandleRuleSaveAsync`; add `ConflictCell` record |
| `postman/postman-Nova.OpsGroups.Api.json` | Add 409 example to `SlaRuleSave` |
| `docs/deferred-work.md` | Remove or mark done once implemented |
