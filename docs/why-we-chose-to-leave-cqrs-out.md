# Why We Chose to Leave CQRS Out

**Decision date:** April 2026  
**Status:** Confirmed  
**Applies to:** All Nova services

---

## Decision

No formal CQRS. No MediatR. The separation of concerns that CQRS provides is achieved informally through FastEndpoints' one-class-per-operation structure and Dapper's per-use-case SQL.

---

## Background

The question was raised by a senior engineer advocating for MediatR + CQRS as the standard .NET pattern. The concern was whether leaving it out was wise. This document records the reasoning so the decision is not revisited without new information.

---

## Nova Already Has Informal CQRS

The separation CQRS mandates already exists in practice:

| Concern | Nova's current approach |
|---|---|
| Reads | POST endpoints, Dapper, SQL optimised per use case, projection variants where needed |
| Writes | PATCH endpoints, explicit SQL, optimistic concurrency, audit trail rows |
| Separation | Each endpoint class owns its own DTO and SQL — read DTOs do not bleed into write paths |

This is CQRS. It is not formalised with command/query objects and a dispatcher, but the structural guarantees are identical.

---

## Why Not Add MediatR to Formalise It

### The double-handler problem

FastEndpoints already provides a handler boundary — each endpoint class is the handler for exactly one operation. Adding MediatR creates:

```
HTTP Request
  → FastEndpoints endpoint       (handler 1)
    → MediatR dispatcher
      → MediatR handler          (handler 2)
```

Two handler layers, no new capability. Tracing any request through the system requires crossing two dispatch mechanisms.

### File explosion per endpoint

Currently one endpoint = one file (~60–80 lines, self-contained). With MediatR, every operation requires a request object, a response object, a handler class, and registration wiring — roughly 3–4 files and 120–160 lines. Across Nova's ~80+ planned endpoints, that is conservatively 200–300 additional hours of initial implementation, producing no user-visible feature.

### Cross-cutting concerns are already handled

The standard MediatR pitch is: attach logging, validation, and timing to the pipeline once. FastEndpoints already provides that pipeline. Auth, rate limiting, SQL logging, and request validation are wired there. MediatR would add a second pipeline with identical responsibilities.

### The indirection cost is permanent

`sender.Send(new GetDeparturesQuery(...))` gives no compile-time path to the handler. Every new developer pays a discovery cost on every feature — they must know the convention or use IDE tooling. This cost is low for any individual instance and high when multiplied across a codebase lifetime.

### Handler isolation tests add coverage without replacing integration tests

Nova's test approach (established across Shell.Api and Presets.Api) tests through HTTP — `WebApplicationFactory` with real HTTP calls against the full pipeline. This catches endpoint binding, validation, auth, and DB behaviour in one test.

MediatR handlers tested in unit isolation test none of those things. You would need unit tests for each handler (new work) on top of the existing integration tests (unchanged work). More tests, more maintenance, same coverage.

In Nova specifically, handlers contain Dapper SQL calls. Unit-testing those in isolation requires mocking the DB connection — which the team has explicitly avoided after a prior incident where mock tests passed but a production migration failed.

---

## The Test Before Reconsidering

If a senior engineer advocates for MediatR, ask them to name a specific problem it solves in Nova that is not already addressed:

| Likely answer | Nova's current answer |
|---|---|
| Separation of reads and writes | Already separated — POST for reads, PATCH for writes, each in its own endpoint class |
| Cross-cutting concerns | FastEndpoints pipeline handles auth, rate limiting, SQL logging |
| Discoverability | One endpoint class per operation — grep for route or class name |
| Testability | Integration tests through WebApplicationFactory, documented in `test-conventions.md` |
| Handler isolation | No domain logic in handlers — Dapper SQL is not meaningfully unit-testable without a real DB |

If the answer is a problem not in that table, that is the conversation worth having. If the answer is "it is the standard pattern," the counter is: FastEndpoints is also a well-established .NET pattern, and it provides the same structural guarantees with half the indirection.

---

## When to Revisit

MediatR earns its place in two specific scenarios:

1. **Large teams (10+ developers)** where discoverability across a sprawling codebase is a daily friction point.
2. **Services with real domain logic in handlers** — not SQL calls, but business rules, state machines, domain events.

Nova is neither of those currently. If it grows to that scale, adopting MediatR then costs far less than adopting it prematurely and maintaining the overhead across every service built in the interim.

---

## Summary

| Concern | Formal CQRS + MediatR | Current approach |
|---|---|---|
| Read/write separation | Explicit command/query types | POST vs PATCH endpoints, Dapper SQL per use case |
| Cross-cutting concerns | MediatR pipeline behaviours | FastEndpoints pipeline |
| Files per endpoint | 3–4 (request, response, handler, registration) | 1 |
| Handler layers | 2 (FastEndpoints + MediatR) | 1 |
| Dev onboarding | Two dispatch patterns to learn | One |
| Test strategy | Unit tests + integration tests | Integration tests only |
| Migration cost (50+ existing endpoints) | High — full refactor | None |
