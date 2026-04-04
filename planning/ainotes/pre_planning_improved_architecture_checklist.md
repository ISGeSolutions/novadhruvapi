# Architecture & Implementation Review Checklist
## Lean Clean / Dapper / Outbox / Multi-tenant .NET APIs

This checklist is intended to be used:
- **Before code generation** (as constraints)
- **After code generation** (as a review gate)

The checklist applies to .NET 10.0+ APIs in a microservices architecture with shared infrastructure.

---

## 1. Architectural Boundaries

### Must Have
- [ ] Domain layer contains no infrastructure concerns (no using statements for logging, HTTP, DB)
- [ ] Application layer contains use-case orchestration only
- [ ] Infrastructure layer contains DB, messaging, cache, external integrations
- [ ] API layer contains no business rules (only routing, model binding, response formatting)
- [ ] Dependency direction is strictly inward (API → Application → Domain)
- [ ] Each layer references only the layer immediately below it

### Red Flags
- Controllers/endpoints calling SQL directly
- Domain referencing logging, configuration, or DB libraries
- Application layer directly referencing external service implementations
- Circular dependencies between layers

---

## 2. Clean Architecture (Lean, Not Ardalis-Heavy)

### Must Have
- [ ] No generic IRepository<T> without justification (multiple implementations exist)
- [ ] Interfaces created only when multiple implementations are plausible
- [ ] Specification pattern used only if queries are reused across multiple places
- [ ] MediatR/mediator pattern used only where it adds value (not for simple CRUD)
- [ ] Structure optimised for discoverability, not pattern purity
- [ ] Repository interfaces justified by multi-DB support or testing strategy
- [ ] Read model (queries) uses direct SQL with DTOs
- [ ] Write model (commands) uses repositories with aggregates

### Red Flags
- Interface-for-everything mentality
- Specification objects wrapping trivial WHERE clauses
- Generic repositories with 20+ methods
- Layers added "just because Clean Architecture"
- Repository methods like `GetAll()`, `Find(Expression<Func<T, bool>>)`

---

## 3. Data Access (Explicit SQL / Dapper)

### Must Have
- [ ] No Entity Framework or EF Core anywhere in the solution
- [ ] Explicit SQL used for all DB interactions (readable, auditable)
- [ ] Safe reader access used (null checks, type conversions)
- [ ] Transaction boundaries explicit and visible (`TransactionScope`, `IDbTransaction`)
- [ ] No hidden Unit of Work abstraction
- [ ] SQL parameters always used (never string concatenation)
- [ ] Dapper or ADO.NET used exclusively for data access
- [ ] Connection disposal handled properly (`using` statements or `IAsyncDisposable`)
- [ ] Command timeout configurable

### Red Flags
- DbContext-style abstractions
- Implicit transactions
- SQL injection vulnerabilities (string concatenation in queries)
- Magic strings for column names without constants
- Large result sets loaded into memory without pagination
- N+1 query problems

---

## 4. Multi-Database Portability

### Must Have
- [ ] SQL limited to a portable subset (SQL-92 compatible where possible)
- [ ] ISqlDialect abstraction exists for DB-specific SQL
- [ ] Date/time handling explicit and consistent (always UTC in DB)
- [ ] UUID/identity strategy DB-agnostic (or abstracted per dialect)
- [ ] Pagination portable across DBs (abstracted via ISqlDialect)
- [ ] DB-specific logic isolated to dialect implementations
- [ ] Integration tests cover all target DB engines (SQL Server, PostgreSQL, MySQL)
- [ ] Migrations written as code with dialect-aware SQL generation
- [ ] No use of `TOP N` (SQL Server only) without abstraction
- [ ] No use of `RETURNING` (PostgreSQL) without abstraction
- [ ] No database-specific functions (`GETDATE()`, `NOW()`) in queries

### Red Flags
- Hard-coded SQL Server syntax (TOP, GETDATE, UNIQUEIDENTIFIER)
- Assuming specific transaction isolation levels
- Using database-specific features without abstraction
- Tests only run against one database provider

---

## 5. Multi-Tenancy (Shared DB)

### Must Have
- [ ] `tenant_id` column mandatory in all business tables
- [ ] Tenant scoping enforced in all queries (compile-time validation where possible)
- [ ] No cross-tenant data leakage possible (tested)
- [ ] Tenant context resolved once per request (from JWT claim)
- [ ] Tenant isolation testable (integration tests verify isolation)
- [ ] ITenantContext injected as scoped dependency
- [ ] All queries include `tenant_id` in WHERE clause
- [ ] Tenant resolution middleware runs early in pipeline
- [ ] Failed tenant resolution returns 401/400, not 500
- [ ] Tenant ID included in all logs, traces, and metrics

### Red Flags
- Queries without `tenant_id` filter
- Tenant context accessed directly from HttpContext in business logic
- Cross-tenant data visible in test scenarios
- Admin/system operations bypassing tenant scoping without audit trail
- Tenant ID passed as method parameters instead of using ITenantContext

---

## 6. Messaging & Outbox (RabbitMQ)

### Must Have
- [ ] Transactional Outbox pattern implemented
- [ ] Outbox table includes `tenant_id`, `aggregate_id`, `event_type`, `payload`
- [ ] Outbox writes occur in same DB transaction as business data
- [ ] No direct RabbitMQ publish inside request transaction
- [ ] Reliable background publisher with retry logic and exponential backoff
- [ ] Messages include `tenant_id`, `message_id`, `correlation_id`
- [ ] Consumers are idempotent (inbox pattern or deduplication)
- [ ] Dead-letter handling for failed messages after N retries
- [ ] Publisher observability (metrics for published, failed, retried messages)
- [ ] Message schema versioning considered

### Red Flags
- Publishing directly to RabbitMQ in transaction
- No idempotency guarantees
- Outbox publisher missing retry logic
- No dead-letter handling
- Missing correlation IDs (can't trace requests across services)
- Outbox table missing indexes (performance issues at scale)

---

## 7. Shared Project (Cross-Cutting Only)

### Must Have
- [ ] Shared project has no domain logic or business rules
- [ ] Shared project depends on no API/domain projects
- [ ] Logging, OTel, auth helpers, caching, outbox infrastructure live here
- [ ] No feature-specific DTOs in shared project
- [ ] Clear versioning strategy (SemVer)
- [ ] Shared project split by concern (Core, Data, Messaging, Observability)
- [ ] Implementations in separate packages (e.g., SendGrid, MsGraph)
- [ ] Each service explicitly chooses which shared implementations to use
- [ ] Shared packages have minimal dependencies

### Red Flags
- Business logic leaking into shared project
- Shared project becoming a "god library"
- Feature-specific code in shared project
- Tight coupling between services via shared DTOs
- Shared project depending on specific API projects
- No versioning strategy (breaking changes force all services to upgrade)

---

## 8. Logging

### Must Have
- [ ] Request/response logging is config-driven (enable/disable per environment)
- [ ] Time-window-based logging supported (peak hours, debug periods)
- [ ] Diagnostic logging optional and configurable per namespace
- [ ] Audit vs debug logs separated (different sinks/retention)
- [ ] Structured logging used (not string interpolation)
- [ ] Sensitive data not logged (passwords, tokens, PII)
- [ ] Log levels used appropriately (Debug, Information, Warning, Error, Critical)
- [ ] Correlation IDs present in all log entries
- [ ] Tenant ID included in all log entries
- [ ] Exception details logged at Error level with stack traces

### Red Flags
- Logging sensitive data (connection strings, passwords, tokens)
- String interpolation instead of structured logging
- Logging in tight loops (performance issues)
- No correlation IDs (can't trace requests)
- Inconsistent log levels

---

## 9. Caching

### Must Have
- [ ] Transactional endpoints never cached
- [ ] Cache profiles defined in configuration (not hard-coded)
- [ ] Runtime cache overrides supported (disable per profile, adjust TTLs)
- [ ] Cache invalidation strategy defined (event-driven, time-based, manual)
- [ ] Optional dry-run mode supported (measure hit/miss without serving from cache)
- [ ] Three-tier caching strategy: HTTP → In-Memory → Redis
- [ ] Cache keys include tenant_id where appropriate
- [ ] Cache warming for critical data on startup
- [ ] Cache metrics exposed (hit/miss rate, evictions)
- [ ] Cache profiles applied via attributes or conventions

### Red Flags
- Caching transactional data
- Hard-coded TTLs
- No cache invalidation strategy
- Cache poisoning possible (bad data cached indefinitely)
- Cache keys not tenant-aware (cross-tenant data leakage)
- No way to disable caching in production for debugging

---

## 10. Observability (OpenTelemetry)

### Must Have
- [ ] Logs, traces, metrics correlated via trace_id, span_id
- [ ] Tenant context included in all traces (tenant.id tag)
- [ ] Datadog-compatible OTLP exporter configured
- [ ] Custom application metrics defined (request count, duration, errors per tenant)
- [ ] Database queries traced with SQL statements
- [ ] HTTP client calls traced
- [ ] Background services instrumented
- [ ] Span attributes include: tenant.id, user.id, correlation.id
- [ ] Error spans marked appropriately
- [ ] Sampling strategy defined (100% in dev, configurable in prod)

### Red Flags
- Traces missing tenant context
- No custom metrics (only framework metrics)
- Database queries not traced
- Sensitive data in span attributes (SQL parameters with PII)
- No correlation between logs and traces

---

## 11. Security

### Must Have
- [ ] JWT authentication enforced on all endpoints (except health checks)
- [ ] Tenant claim extracted from JWT and validated
- [ ] User claims extracted and available via ICurrentUser
- [ ] Secrets decrypted via approved mechanism (Cipher.Decrypt for "ENC:" prefix)
- [ ] No secrets logged (connection strings, API keys, tokens)
- [ ] Auth and tenant context separated (different services)
- [ ] HTTPS enforced in production
- [ ] CORS configured appropriately (not wildcard in production)
- [ ] Input validation on all endpoints (FluentValidation or data annotations)
- [ ] Authorization policies defined and enforced

### Red Flags
- Secrets in plain text in configuration files
- Secrets logged in exception messages
- Tenant ID accepted from query string/body instead of JWT
- No input validation
- Wildcard CORS in production
- Authentication bypassed in code paths

---

## 12. Configuration

### Must Have
- [ ] Configuration split: application config vs operational config
- [ ] Operational config supports hot-reload (logging, caching)
- [ ] Configuration validation on startup (fail-fast if invalid)
- [ ] Encrypted values prefixed with "ENC:" and decrypted at startup
- [ ] Environment-specific overrides (appsettings.{Environment}.json)
- [ ] Sensitive config not committed to source control
- [ ] Configuration documented (what each setting does)

### Red Flags
- Hard-coded connection strings, API keys
- No configuration validation
- Breaking changes in config require code changes
- Secrets in source control
- Configuration changes require application restart for operational settings

---

## 13. API Design

### Must Have
- [ ] API versioning implemented (URL-based: /api/v1/...)
- [ ] RESTful conventions followed (GET, POST, PUT, DELETE)
- [ ] Consistent response formats (success, error, validation errors)
- [ ] HTTP status codes used correctly (200, 201, 204, 400, 401, 403, 404, 500)
- [ ] Pagination supported for list endpoints (page, pageSize)
- [ ] Input validation with clear error messages
- [ ] OpenAPI/Swagger documentation generated
- [ ] Endpoint naming consistent and discoverable
- [ ] No breaking changes in same API version

### Red Flags
- Inconsistent HTTP status codes
- No API versioning (impossible to evolve API)
- List endpoints without pagination
- Generic error messages ("An error occurred")
- Exposing internal exception details to clients

---

## 14. Error Handling

### Must Have
- [ ] Global exception handling middleware
- [ ] Domain exceptions mapped to appropriate HTTP status codes
- [ ] Validation errors return 400 with structured error details
- [ ] Unhandled exceptions return 500 without exposing internals
- [ ] Errors logged with full context (tenant, user, correlation ID)
- [ ] ProblemDetails format used for errors
- [ ] Custom exception types for domain errors
- [ ] Transient errors handled with retry policies (Polly)

### Red Flags
- Stack traces exposed to clients
- Generic catch-all exception handlers that swallow errors
- No distinction between client errors (4xx) and server errors (5xx)
- Exceptions used for control flow
- No logging of exceptions

---

## 15. Testing Strategy

### Must Have
- [ ] Unit tests for domain logic (no external dependencies)
- [ ] Integration tests for repositories (real DB via TestContainers)
- [ ] Integration tests run against all DB providers
- [ ] API tests for critical endpoints
- [ ] Tenant isolation verified in tests
- [ ] Test coverage for error scenarios
- [ ] Tests are fast, isolated, and repeatable
- [ ] CI/CD pipeline runs all tests

### Red Flags
- No tests
- Tests requiring manual database setup
- Tests only run against one DB provider
- Tests sharing state (not isolated)
- Flaky tests
- Tests that only cover happy paths

---

## 16. Performance

### Must Have
- [ ] Pagination implemented for all list endpoints
- [ ] Database indexes on frequently queried columns (tenant_id, foreign keys)
- [ ] Async/await used correctly (no blocking calls)
- [ ] Connection pooling enabled
- [ ] N+1 query problems identified and fixed
- [ ] Caching used appropriately for read-heavy endpoints
- [ ] Response compression enabled
- [ ] Large payloads streamed (not loaded into memory)

### Red Flags
- Loading entire tables into memory
- Synchronous I/O (blocking threads)
- Missing indexes on tenant_id
- N+1 queries
- No pagination
- No caching for expensive read operations

---

## 17. AI-Generated vs Human-Owned Code

### Must Have
- [ ] AI-generated code isolated in Generated/ folders
- [ ] Generated files include header comment indicating auto-generation
- [ ] Domain logic and orchestration human-reviewed
- [ ] Regeneration does not overwrite core business logic
- [ ] Class/method XML documentation present and accurate
- [ ] Clear ownership boundaries (what can be regenerated vs what can't)
- [ ] Generated code follows same conventions as human-written code

### Red Flags
- No distinction between AI and human code
- Business logic in generated files
- Generated code violates architectural principles
- No documentation on what's safe to regenerate

---

## 18. Code Quality

### Must Have
- [ ] British English used for all identifiers (authorise, colour, initialise)
- [ ] File-scoped namespaces used
- [ ] Nullable reference types enabled (#nullable enable)
- [ ] CancellationToken parameters on all async methods
- [ ] XML documentation on all public APIs
- [ ] No magic strings or numbers (use constants)
- [ ] SOLID principles followed
- [ ] Code is readable and self-documenting
- [ ] Consistent naming conventions (PascalCase, camelCase)
- [ ] No code duplication (DRY principle)

### Red Flags
- American spelling in code (color, authorize)
- Missing XML documentation
- Magic strings/numbers everywhere
- Inconsistent naming
- Large methods (>50 lines)
- Deep nesting (>3 levels)
- Code smells (god classes, feature envy, shotgun surgery)

---

## 19. Deployment & Operations

### Must Have
- [ ] Health check endpoints implemented (/health, /health/live, /health/ready)
- [ ] Graceful shutdown implemented
- [ ] Docker support (Dockerfile)
- [ ] Environment-specific configuration
- [ ] Startup validation (fail-fast on missing/invalid config)
- [ ] Console mode for debugging (--console flag)
- [ ] Application version exposed in API response or header

### Red Flags
- No health checks
- Application doesn't shut down gracefully
- No containerisation strategy
- Application starts with invalid configuration
- No way to verify which version is deployed

---

## 20. Documentation

### Must Have
- [ ] README.md with setup instructions
- [ ] Architecture decision records (ADRs) for key decisions
- [ ] API documentation (OpenAPI/Swagger)
- [ ] Configuration documentation (what each setting does)
- [ ] Deployment guide
- [ ] Troubleshooting guide
- [ ] Database schema documentation
- [ ] Messaging contract documentation

### Red Flags
- No README
- No documentation of architectural decisions
- Configuration settings undocumented
- No troubleshooting guide
- Outdated documentation

---

## Overall Assessment

### Severity Levels
- **Critical**: Architectural violations, security issues, data loss risks
- **High**: Performance problems, maintainability issues
- **Medium**: Code quality, testing gaps
- **Low**: Documentation, minor inconsistencies

### Decision Matrix
- [ ] **PASS** – Ready to proceed (0 critical, ≤2 high issues)
- [ ] **CONDITIONAL PASS** – Issues documented, remediation plan in place (≤1 critical, ≤5 high issues)
- [ ] **FAIL** – Major architectural violations, requires redesign (>1 critical or >5 high issues)

### Issues Found
| Category | Severity | Description | Remediation |
|----------|----------|-------------|-------------|
| | | | |

### Sign-off
- [ ] Architecture Reviewed By: _________________ Date: _________
- [ ] Security Reviewed By: _________________ Date: _________
- [ ] Performance Reviewed By: _________________ Date: _________