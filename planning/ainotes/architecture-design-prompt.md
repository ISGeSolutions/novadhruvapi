# .NET 10+ Microservices API Architecture Design Prompt

## Purpose
This document is a **reusable architecture-design prompt** for initiating a new .NET 10.0+ API project within a microservices ecosystem.  
It is intended to drive **architecture, structure, and design discussion only**, **no code generation**.

---

## Prompt

I want to design a **.NET 10.0+ API** using the latest version of C#.

This API will be used for a **commercial product** and will be part of a **microservices architecture** with **10+ API services**, one per domain.

The API is:
- **Multi-tenant**
- Uses a **shared database** with a mandatory `tenant_id` column
- Designed for **highly transactional workloads** (bookings, financials, stateful workflows)

**Do not generate any code.**  
Focus only on **architecture, project structure, patterns, best practices, and trade-offs**.

---

## Shared Code & Project Structure

Because there will be 500+ API services, there will be common cross-cutting functionality shared across all APIs.  
**Duplicate implementations across services are not acceptable.**

The design must include:
- A **shared project/library** used by all API services
- Clear rules on what **can** and **cannot** live in the shared project

### Shared project principles
The shared project:
- Contains **cross-cutting and infrastructural concerns only**
- Must have **no domain knowledge**
- Must not depend on any specific API or domain project
- Must avoid becoming a god library

Acceptable shared concerns include:
- Logging (request/response, diagnostics, file/db sinks)
- OpenTelemetry setup and helpers
- JWT authentication & authorisation helpers
- Tenant resolution & tenant context
- Configuration loading, hot-reload, and validation
- Caching abstractions and cache profile infrastructure
- RabbitMQ client wrappers/abstractions
- Transactional Outbox infrastructure
- Correlation IDs and tracing context
- Health check primitives
- Common middleware

Non-acceptable shared code includes:
- Domain entities or aggregates
- Business rules
- Domain services
- Feature-specific DTOs
- Use-case orchestration logic

### Dependency direction
- API projects may depend on the shared project
- Domain and application layers must not depend on other API projects
- The shared project must not depend on any API or domain project

### Versioning & evolution
- The shared project should be versioned independently
- API services should be able to upgrade shared code independently
- Backward compatibility should be considered in the design

Please discuss:
- Recommended solution / project layout for a repo containing multiple services
- How to prevent tight coupling via the shared project
- How shared infrastructure integrates with a lean Clean Architecture approach
- Whether the shared project should be consumed via **internal NuGet packages** or **project references**, and when to split it further

---

## Architecture

The project should follow **Clean Architecture principles**, **inspired by** the Ardalis Clean Architecture template.
URL: https://github.com/ardalis/CleanArchitecture

Key architectural intent:
- Prefer **lean Clean Architecture**
- Avoid unnecessary abstractions
- Avoid interface-for-everything
- Prioritise clarity, explicit transaction boundaries, and testability

Constraints:
- **No Entity Framework / EF Core**
- Data access via **explicit SQL**
- Dapper is acceptable; if not used, propose an equivalent **micro-ORM** approach
- No generic repositories unless justified by multiple implementations

English (UK) must be used for:
- Variable names
- Class names
- Method names
- Identifiers

---

## Database & Transactions

- Data access must use `Microsoft.Data.SqlClient` (or equivalent DB-specific drivers).
- Queries and commands must be explicit and readable.
- Safe reader access must be used when reading column values.
- **Pragmatic CQRS**:
  - Commands mutate state
  - Queries return projections / DTOs
  - No event sourcing
  - No unnecessary framework-driven CQRS ceremony

The API must be portable across:
- SQL Server (primary)
- PostgreSQL
- MySQL

Please discuss best practices for:
- Confirm Key architectural intent: meets the Clean Architecture principles as per Ardalis
- SQL portability and a recommended **portable SQL subset**
- Confirm - **Pragmatic CQRS**: meets the Clean Architecture principles as per Ardalis
- Datetime handling and timezone expectations
- Identity / UUID handling across DBs
- Pagination patterns across DBs
- Transaction boundaries and isolation considerations
- How to run migrations / schema evolution safely across DB engines
- Testing strategy (integration tests against all DB engines)
- **Tenant isolation enforcement** (e.g. tenant scoping guards, query validation, testing strategy)

---

## Messaging & Outbox

The organisation already uses **RabbitMQ**.

Messaging intent:
- Messaging is **asynchronous**
- Used for **side-effects and cross-service integration only**
- Core transactions must remain synchronous and deterministic

The design **must include the Transactional Outbox pattern**:
- Outbox table lives in the same shared database
- Outbox writes occur in the same DB transaction as business data
- Message publishing happens after commit
- No direct publishing to RabbitMQ inside the request transaction

Please discuss:
- Outbox table structure (including `tenant_id`)
- Publishing strategy, batching, locking, and retry backoff
- Failure handling and observability
- Idempotency expectations for consumers (e.g. inbox table keyed by `(tenant_id, message_id)`)
- Routing strategy in RabbitMQ (exchanges, routing keys, tenant-aware routing without creating per-tenant queues unless necessary)

---

## Configuration

I prefer **two configuration files**:
1. Application configuration (DB, tenants, core settings)
2. Operational configuration (logging, caching)

Operational configuration must:
- Support **hot loading**
- Validate before applying
- Roll back to last known-good configuration if validation fails

---

## Logging

Logging requirements:
- Optional request/response logging to database (config-driven)
- Ability to configure **multiple time windows** (start/end date-time) for logging:
  - peak periods
  - low periods
- Optional diagnostic logging in code flow (config-driven)
- File-based logs with clear naming conventions
- Clear separation between:
  - audit / operational logs
  - debug / diagnostic logs

---

## Caching

Caching layers:
1. HTTP caching for public/static data
2. In-memory cache for instance-scoped data
3. Redis for shared cache across instances

Requirements:
- Cache configuration in application config
- Cache diagnostics in operational config
- Cache-aside pattern
- Event-driven invalidation where feasible
- Cache warming for critical data on startup

There will be **500+ endpoints**:
- Some must never be cached (transactional)
- Some can be cached safely (reference / preset data)
- Each micro-service project will contain domain specific calls and we will try and limit to approx 50 end-points

Design requirements:
- Cache profiles defined in configuration
- Profiles applied via attributes or conventions
- Runtime overrides:
  - Disable caching per endpoint
  - Adjust TTLs without redeployment
  - Disable caching globally
- Optional **dry-run caching mode** (measure hit/miss without serving from cache)
- Ability to auto-generate an endpoint inventory (including cacheability)

---

## Observability

- Instrument the API using **OpenTelemetry**
- Assume **Datadog** as the OTel collector
- Discuss:
  - tracing
  - metrics
  - log correlation
  - multi-tenant observability considerations

---

## Security

- JWT-based authentication and authorisation
- All secrets (DB connection strings, keys) will be decrypted using an existing `cipher.cs`
- Do not design a new encryption mechanism

---

## Code Structure & AI Usage

I want a clear strategy for grouping source files into:
1. **AI-generated, easily replaceable scaffolding**
2. **Human-reviewed, complex logic** (domain rules, transactions, orchestration)

Please discuss:
- Folder / project structure
- Ownership boundaries
- How to safely regenerate AI-owned code without damaging core logic
- How to keep documentation at class/method level consistent and maintainable

---

## Development & Debugging

- The API may also be run as a **console application**
- In this mode, detailed startup and runtime feedback should be available to aid debugging

---

## Initial Shell

The recommended starting point:
- The **shared project/library** used by all API services
- A second project, a shell API project that refers to shared-project (with cross-cutting concerns) with end-points
- `GET /hello-world`
- `GET /test-db/mssql` that executes a simple query:  
  `SELECT code, value FROM sales97.dbo.pointer`
- `GET /test-db/postgres` that executes a simple query:  
  `SELECT code, value FROM sales97.public.pointer`
- `GET /test-db/mysql` that executes a simple query:  
  `SELECT code, value FROM sales97.pointer`

- Separate health check endpoints for each DB engine
- All other endpoints use **one DB selected via configuration**

---

**Do not generate code.**  
Please focus on **architecture, structure, trade-offs, and rationale**.