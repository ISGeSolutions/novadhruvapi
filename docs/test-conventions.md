# Nova Test Conventions

## Purpose

This document is the single source of truth for how tests are written across the Nova platform.
All AI-generated and human-authored tests must follow these conventions without exception.
Quality of tests is non-negotiable.

---

## 1. Guiding Principles

- **Readability first.** A test must be understood by a .NET or Python developer without prior
  context. If a test requires a comment to explain what it does, rename it or restructure it.
- **Explicit over implicit.** Everything a test needs must be visible in the test method itself,
  or in a helper called explicitly from it. Nothing is hidden in base classes or shared fixtures.
- **Test the HTTP contract.** Tests assert what the caller observes: status codes, response body
  shape, and headers. They never assert on internal state, private methods, or DI registrations.
- **Isolation.** Each test is fully independent. No test must pass for another to pass. No shared
  mutable state between tests.

---

## 2. Naming — Given/When/Then

Test class name = the system under test (one class per endpoint file):

```
HelloWorldEndpointTests
EchoEndpointTests
TestCacheEndpointTests
```

Test method name follows `Given_..._When_..._Then_...`:

```csharp
Given_ValidRequest_When_HelloWorldIsCalled_Then_Returns200WithCorrectBody
Given_MissingTenantId_When_EchoIsCalled_Then_Returns400
Given_NoToken_When_EchoIsCalled_Then_Returns401
Given_ExpiredToken_When_EchoIsCalled_Then_Returns401
Given_RedisAvailable_When_TestCacheIsCalled_Then_Returns200WithCachedTimestamp
Given_RedisUnavailable_When_TestCacheIsCalled_Then_Returns200ViaFallback
```

Rules:
- `Given` — the precondition or test data state
- `When` — the action (always the HTTP call)
- `Then` — the observable outcome (status code first, then body if relevant)
- Use plain English words, not abbreviations
- Never use `Test` as a prefix on the method name — xUnit does not require it and it adds noise

---

## 3. Test Structure — Arrange / Act / Assert

Every test follows explicit AAA with section comments. No exceptions.

```csharp
[Fact]
public async Task Given_ValidRequest_When_HelloWorldIsCalled_Then_Returns200WithCorrectBody()
{
    // Arrange
    var client = TestHost.Create().CreateClient();

    // Act
    var response = await client.GetAsync("/api/v1/hello-world");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<HelloWorldResponse>(JsonOptions.Default);
    body!.Message.Should().Be("Hello, World!");
}
```

Rules:
- Always use `// Arrange`, `// Act`, `// Assert` comments — they act as visual anchors for
  developers unfamiliar with .NET test conventions (including Python devs reading the suite)
- Each section is a distinct block; never merge them
- The `// Act` block is always a single statement (the HTTP call)

---

## 4. Test Host — Thin Static Factory

`TestHost` is a **static class**, not a base class or fixture. It creates a
`WebApplicationFactory<Program>` for the service under test with the minimum necessary overrides.

### Standard pattern (no Redis, no email)

```csharp
// Helpers/TestHost.cs
public static class TestHost
{
    public static WebApplicationFactory<Program> Create() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.ConfigureServices(services =>
                {
                    // Infrastructure-only override: replace the cipher so tests do not
                    // need the ENCRYPTION_KEY environment variable. The test project's
                    // appsettings.json supplies plaintext values; PassthroughCipherService
                    // returns them as-is.
                    services.AddSingleton<ICipherService, PassthroughCipherService>();
                });
            });

    // Redis-aware overload: used by tests that exercise caching or locking endpoints.
    public static WebApplicationFactory<Program> CreateWithRedis(string redisConnectionString) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<ICipherService, PassthroughCipherService>();
                });
                builder.UseSetting("ConnectionStrings:redis", redisConnectionString);
            });
}
```

### Extended pattern — when the service has an encrypted `Jwt:SecretKey` or sends email

`WebApplicationFactory<Program>` loads the **service's** content root `appsettings.json`, not
the test project's. If the service's `appsettings.json` has an encrypted `Jwt:SecretKey`,
`PassthroughCipherService` returns it unchanged — causing JWT signature mismatches on every
authenticated test. Fix with `ConfigureAppConfiguration` + `AddInMemoryCollection` to inject
plaintext overrides that take precedence over all `appsettings.json` values.

```csharp
public static WebApplicationFactory<Program> Create() =>
    new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");

            // Override JWT settings — takes precedence over the service's appsettings.json.
            // Required because WebApplicationFactory loads the SERVICE's content root config
            // (which has an encrypted SecretKey), not the test project's appsettings.json.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:SecretKey"]  = TestConstants.JwtSecret,
                    ["Jwt:Issuer"]     = TestConstants.JwtIssuer,
                    ["Jwt:Audience"]   = TestConstants.JwtAudience,
                });
            });

            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ICipherService, PassthroughCipherService>();

                // Discard outbound email — no SendGrid API key required.
                services.AddSingleton<IEmailSender, NoOpEmailSender>();
            });
        });
```

**`NoOpEmailSender`** is a minimal infrastructure override for services that send email:

```csharp
internal sealed class NoOpEmailSender : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
        => Task.CompletedTask;
}
```

Rules:
- **DI overrides are allowed only for infrastructure** (cipher, email, external connections).
  Never for business logic, validators, or domain services.
- Use `ConfigureAppConfiguration` + `AddInMemoryCollection` (not `UseSetting`) when you need
  to override values that are read during `WebApplicationFactory` host construction.
- The factory is disposable — call `await factory.DisposeAsync()` in `DisposeAsync()` if the
  test class implements `IAsyncLifetime`
- Do not subclass `WebApplicationFactory<Program>` — it hides the override from the reader

---

## 5. Test Constants

All fixed test values live in one visible file. Never hardcode strings in test methods.

```csharp
// Helpers/TestConstants.cs
public static class TestConstants
{
    // JWT — injected via ConfigureAppConfiguration in TestHost; must match JwtFactory parameters.
    public const string JwtSecret   = "nova-test-signing-key-minimum-32-chars-x";
    public const string JwtIssuer   = "https://auth.nova.internal";
    public const string JwtAudience = "nova-api";

    // RequestContext — all four fields are required by RequestContextValidator.
    // Services that use AddNovaTenancy() also require TenantId to match a JWT claim.
    public const string TenantId    = "BTDK";
    public const string CompanyId   = "COMPANY-001";
    public const string BranchId    = "BRANCH-001";
    public const string UserId      = "test-user-001";
}
```

Note: `CompanyId` and `BranchId` were added when writing Presets.Api tests. `RequestContextValidator`
requires all four fields (`tenant_id`, `company_id`, `branch_id`, `user_id`) — omitting any returns
400. Include all four in every service's `TestConstants`.

---

## 6. JWT Factory — Explicit Parameters

Every call to `JwtFactory.CreateToken` passes values explicitly. No hidden defaults loaded from
config. This makes the test self-documenting — a reader can see exactly what claims the token
carries without opening another file.

```csharp
// Helpers/JwtFactory.cs
var token = JwtFactory.CreateToken(
    tenantId: TestConstants.TenantId,
    userId:   TestConstants.UserId,
    secret:   TestConstants.JwtSecret,
    issuer:   TestConstants.JwtIssuer,
    audience: TestConstants.JwtAudience);
```

`JwtFactory.CreateToken` generates a signed JWT with a 1-hour lifetime. It never reads from
configuration or environment variables.

Rationale: authentication is security-critical. Explicit parameters ensure a failing auth test
displays every relevant value in the test output without requiring the reader to trace config files.

---

## 7. Redis Fixture

`RedisFixture` wraps the Testcontainers Redis lifecycle. It is declared **explicitly** in each
test class that needs Redis — never shared across unrelated test classes.

```csharp
// Helpers/RedisFixture.cs — defined once
public sealed class RedisFixture : IAsyncLifetime
{
    public RedisContainer Container { get; } = new RedisBuilder().Build();
    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync() => await Container.StartAsync();
    public async Task DisposeAsync()   => await Container.DisposeAsync();
}

// In a test class that needs Redis — explicit declaration, explicit lifecycle
public class TestCacheEndpointTests : IAsyncLifetime
{
    private readonly RedisFixture _redis = new();

    public async Task InitializeAsync() => await _redis.InitializeAsync();
    public async Task DisposeAsync()    => await _redis.DisposeAsync();

    [Fact]
    public async Task Given_RedisAvailable_When_TestCacheGetIsCalled_Then_Returns200()
    {
        // Arrange
        var client = TestHost.CreateWithRedis(_redis.ConnectionString).CreateClient();
        ...
    }
}
```

Rules:
- One `RedisFixture` per test class — never share across classes (isolation)
- Always start and dispose via `IAsyncLifetime` — never in a constructor
- The fixture is an infrastructure concern only; it never carries test data or assertions

---

## 8. Assertions — FluentAssertions

All assertions use **FluentAssertions**. xUnit's built-in `Assert.*` is not used.

```csharp
// Status code
response.StatusCode.Should().Be(HttpStatusCode.OK);

// Body fields
body!.Message.Should().Be("Hello, World!");
body.CorrelationId.Should().NotBeNullOrEmpty();
body.DepDate.Should().Be(new DateOnly(2026, 8, 15));

// UTC timestamp
body.CreatedOn.Offset.Should().Be(TimeSpan.Zero);

// Equivalent body shape (order-independent field comparison)
body.Should().BeEquivalentTo(expected, options => options.ExcludingMissingMembers());

// Negative assertions
response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
```

Rationale:
- **Failure messages.** FluentAssertions names the subject in failures:
  `Expected response.StatusCode to be 200, but found 401` vs `Expected: 200, Actual: 401`.
  On CI without a debugger, this difference matters.
- **`BeEquivalentTo`.** Eliminates field-by-field assertions on response bodies.
  A single call compares the full shape and reports every mismatched field in one failure.
- **Readability.** Subject-first, verb-fluent chains read as English sentences —
  accessible to Python developers reading the .NET suite for the first time.

---

## 9. JSON Deserialisation in Tests

Nova uses snake_case on the wire. Tests must deserialise with the same options.

```csharp
// Helpers/JsonOptions.cs
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}
```

Always use `JsonOptions.Default` when calling `ReadFromJsonAsync<T>`. Never deserialise with
default `JsonSerializerOptions` — the snake_case fields will not bind.

---

## 10. What to Avoid

| Avoid | Why |
|---|---|
| Base test classes | Hides setup; increases cognitive load for Python devs |
| `[Collection]` shared fixtures across files | Creates invisible coupling between test classes |
| Mocking business logic or validators | Tests must exercise the real pipeline |
| DI overrides for domain services | Infrastructure only: cipher, external connections |
| `[Theory]` by default | Use only when data variation is the point, not for every happy path |
| Asserting on internal state | Test the HTTP contract; never reach into the DI container |
| Skipping `// Arrange / Act / Assert` | Mandatory — they are the navigation anchors |
| Hardcoded strings in test methods | Use `TestConstants` |
| `Assert.*` from xUnit | Use FluentAssertions throughout |

---

## 11. File Layout

```
src/
  tests/
    Nova.Shell.Api.Tests/
      appsettings.json           ← minimal config; plaintext secrets (PassthroughCipherService)
      opsettings.json            ← test overrides: OutboxRelay disabled, RateLimiting disabled
      migrationpolicy.json       ← copy of service migrationpolicy.json
      GlobalUsings.cs            ← global using Xunit; (makes [Fact], IAsyncLifetime available project-wide)
      Helpers/
        TestHost.cs              ← WebApplicationFactory thin factory (two overloads)
        TestConstants.cs         ← all fixed test values
        JwtFactory.cs            ← explicit-parameter token generator
        RedisFixture.cs          ← Testcontainers Redis lifecycle wrapper
        PassthroughCipherService.cs ← ICipherService that returns input unchanged
        JsonOptions.cs           ← snake_case JsonSerializerOptions
      Endpoints/
        HelloWorldEndpointTests.cs
        EchoEndpointTests.cs
        TestCacheEndpointTests.cs
      Nova.Shell.Api.Tests.csproj
```

---

## 12. Test Configuration Files

Tests run with `ASPNETCORE_ENVIRONMENT=Test`. The host loads:

1. `appsettings.json` — minimal config, **plaintext** secrets (no encryption needed because
   `PassthroughCipherService` returns the value unchanged)
2. `opsettings.json` — test version with OutboxRelay and RateLimiting disabled
3. `migrationpolicy.json` — identical to service version (policy logic is tested, not the file)

All three files are in the test project root and copied to the output directory on build.

There is no `appsettings.Test.json`. The test project's `appsettings.json` **is** the test config —
it is not an overlay on top of the service's `appsettings.json`. The two files are independent.

---

## 13. Date and Time Assertions

The Nova wire contract drops sub-second precision from `DateTimeOffset` values
(see `UtcDateTimeOffsetConverter` — format is `"yyyy-MM-ddTHH:mm:ssZ"`). When asserting
that a response timestamp falls within a time window, truncate the `before` bound to the
second. Otherwise a `before` captured mid-second will be greater than the truncated response
value, causing a spurious failure even though the server generated the timestamp after `before`.

```csharp
// Correct — truncate to the second to match server serialisation precision
var before = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
var client = TestHost.Create().CreateClient();

var response = await client.GetAsync("/api/v1/hello-world");
var body     = await response.Content.ReadFromJsonAsync<HelloWorldResponse>(JsonOptions.Default);

body!.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow);
```

Rule: always use `DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds())`
to truncate, not `DateTimeOffset.UtcNow`. Never use `DateTime.UtcNow`.
