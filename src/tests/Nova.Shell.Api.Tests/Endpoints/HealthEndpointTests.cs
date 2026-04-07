using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Nova.Shell.Api.Tests.Helpers;

namespace Nova.Shell.Api.Tests.Endpoints;

/// <summary>
/// Phase 1 tests for health check endpoints.
///
/// /health          — all registered checks (Redis + RabbitMQ).
///                    RabbitMQ is not available in the test environment, so this
///                    endpoint returns 503 Unhealthy. We assert the endpoint is
///                    reachable and responds with a valid health status code (not 500).
///
/// /health/redis    — Redis-only check, uses a live container via RedisFixture.
///                    Asserts 200 Healthy when Redis is available.
/// </summary>
public class HealthEndpointTests
{
    // ---------------------------------------------------------------------------
    // /health — combined check (all registered checks)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_AppIsRunning_When_HealthEndpointIsCalled_Then_ReturnsAValidHealthStatusCode()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        // The combined health endpoint returns 200 (Healthy) or 503 (Unhealthy/Degraded).
        // A 500 Internal Server Error means the health check middleware itself has failed —
        // which is a wiring bug, not a dependency outage.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Given_AppIsRunning_When_HealthEndpointIsCalled_Then_ResponseBodyIsJson()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Given_AppIsRunning_When_HealthEndpointIsCalled_Then_ResponseBodyContainsStatusField()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/health");
        var body     = await response.Content.ReadFromJsonAsync<HealthResponse>(JsonOptions.Default);

        // Assert
        body!.Status.Should().BeOneOf("Healthy", "Unhealthy", "Degraded");
    }

    // ---------------------------------------------------------------------------
    // /health/redis — isolated Redis check with a live container
    // ---------------------------------------------------------------------------

    public class RedisHealthTests : IAsyncLifetime
    {
        private readonly RedisFixture _redis = new();

        public async Task InitializeAsync() => await _redis.InitializeAsync();
        public async Task DisposeAsync()    => await _redis.DisposeAsync();

        [Fact]
        public async Task Given_RedisIsAvailable_When_HealthRedisIsCalled_Then_Returns200Healthy()
        {
            // Arrange
            var client = TestHost.CreateWithRedis(_redis.ConnectionString).CreateClient();

            // Act
            var response = await client.GetAsync("/health/redis");
            var body     = await response.Content.ReadFromJsonAsync<HealthResponse>(JsonOptions.Default);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.Status.Should().Be("Healthy");
        }
    }

    // ---------------------------------------------------------------------------
    // /health/rabbitmq — present to confirm the endpoint is wired and reachable.
    // RabbitMQ is not running in the test environment so the check returns Unhealthy.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_AppIsRunning_When_HealthRabbitMqIsCalled_Then_ReturnsAValidHealthStatusCode()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/health/rabbitmq");

        // Assert
        // We confirm the endpoint exists and the middleware responds.
        // Healthy (200) if RabbitMQ is reachable; Unhealthy (503) if not.
        // Never 404 (not wired) or 500 (health check threw an unhandled exception).
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    // ---------------------------------------------------------------------------
    // Shared response shape
    // ---------------------------------------------------------------------------

    private sealed record HealthResponse(string Status);
}
