# Why We Chose Dapper Over EF Core

**Decision date:** April 2026  
**Status:** Confirmed  
**Applies to:** All Nova services

---

## Decision

Dapper + explicit SQL throughout. No EF Core — not for reads, not for writes.

---

## Background

The question was raised specifically about a hybrid approach: keep Dapper for reads and lists (performance), but adopt EF Core for CRUD writes to leverage its built-in optimistic concurrency management (`[ConcurrencyCheck]`, `IsRowVersion()`, `DbUpdateConcurrencyException`).

---

## Why Not EF Core for Writes

### Two data access layers, one broken mental model

Every developer would need to know: reads = Dapper SQL, writes = EF Core DbContext. That boundary is easy to misapply and hard to enforce in code review. Nova currently has one rule with no exceptions.

### Two dialect strategies in parallel

Nova already has `ISqlDialect` for Dapper (`PostgresDialect`, `MariaDbDialect`, `MsSqlDialect`). EF Core uses separate NuGet providers (`Npgsql.EF`, `Pomelo.MySql.EF`, `SqlServer.EF`) with their own quirks per version. Maintaining two parallel dialect abstractions across three databases doubles the surface area for subtle, hard-to-diagnose bugs.

### Two migration systems

Nova uses hand-written versioned SQL migrations (V001, V002…) per dialect, one set per service. EF Core generates its own code-first migrations. Mixing them means two migration systems touching the same schema — the order of application, the source of truth, and the review process become ambiguous.

### MSSQL-LEGACY tables are hostile to EF Core

Legacy columns are PascalCase and inconsistent (`BookingNo`, `BkgNo`). Every property in an EF Core entity would need `HasColumnName("BkgNo")` explicit mapping. The MSSQL-LEGACY audit comment pattern (`// MSSQL-LEGACY. Review aliases...`) has no natural home in EF Core entity configurations. The concurrency benefit largely disappears for legacy tables anyway, since `rowversion` columns rarely exist on them.

### PATCH endpoints fight EF Core's model

Most Nova write endpoints are PATCH (partial update). EF Core change tracking works cleanly when you load the entity first. For partial updates without a full load, you either do a round-trip fetch (extra DB call) or attach a stub entity (error-prone). Dapper's `UPDATE ... SET only_what_changed WHERE ...` is simpler and explicit.

### Concurrency is already solved without EF Core

EF Core's concurrency value is highest when you lack explicit SQL control — it removes the need to hand-craft `AND version = @token`. In Nova, that clause is already the established pattern:

- OpsGroups.Api SLA rule save: version token + `WHERE id = @id AND version = @token`
- OpsGroups.Api bulk task update: reads status before update, rolls back on conflict

EF Core would generate an equivalent `WHERE` clause. Adopting it hides the mechanism without eliminating the underlying check. The explicit version is easier to test and reason about.

---

## What We Do Instead

The version-token optimistic concurrency pattern:

```sql
UPDATE grouptour_sla_rules
SET    new_value = @NewValue, version = @NextVersion
WHERE  id = @Id AND version = @ExpectedVersion
```

If the `UPDATE` affects 0 rows, a conflict is detected and a `409 Conflict` is returned.

A shared `ExecuteWithConcurrencyCheckAsync` helper in `Nova.Shared` is a planned future improvement to standardise this pattern across services (see `project_future_todos.md`).

---

## Summary

| Concern | EF Core hybrid | Dapper only |
|---|---|---|
| Data access mental model | Split (reads vs writes) | Single |
| Dialect handling | Two strategies in parallel | One (`ISqlDialect`) |
| Migration system | Two systems in conflict | One (versioned SQL files) |
| MSSQL-LEGACY support | Needs per-column `HasColumnName` | Alias in SQL, comment in code |
| PATCH / partial update | Round-trip or stub entity | Explicit `SET` clause |
| Optimistic concurrency | `DbUpdateConcurrencyException` | `WHERE id = @id AND version = @v` |
| Dev onboarding | Two ORMs to learn | One |
