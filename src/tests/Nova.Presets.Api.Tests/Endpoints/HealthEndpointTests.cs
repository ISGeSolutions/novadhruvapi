using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Nova.Presets.Api.Tests.Helpers;

namespace Nova.Presets.Api.Tests.Endpoints;

/// <summary>
/// Tests for health check endpoints.
///
/// GET /health     — standard ASP.NET health check, JSON response writer configured.
///                   Returns 200 (Healthy) or 503 (Unhealthy/Degraded).
///
/// GET /health/db  — PresetsDb connectivity check. Returns 503 Unhealthy in the test
///                   environment because the connection string is a test placeholder.
///                   We assert the endpoint is reachable and the error is handled
///                   gracefully (not a 500).
/// </summary>
public class HealthEndpointTests
{
    // ---------------------------------------------------------------------------
    // GET /health — combined ASP.NET health check
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_AppIsRunning_When_HealthEndpointIsCalled_Then_ReturnsAValidHealthStatusCode()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        // The health endpoint returns 200 (Healthy) or 503 (Unhealthy/Degraded).
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
    // GET /health/db — PresetsDb connectivity check
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_AppIsRunning_When_HealthDbEndpointIsCalled_Then_ReturnsAValidDbHealthStatusCode()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/health/db");

        // Assert
        // In the test environment, the PresetsDb connection string is a placeholder.
        // The endpoint catches all exceptions and returns 503 Unhealthy rather than
        // propagating a 500 — we confirm the exception is handled gracefully.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Given_AppIsRunning_When_HealthDbEndpointIsCalled_Then_ResponseBodyContainsDatabaseField()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/health/db");
        var body     = await response.Content.ReadFromJsonAsync<DbHealthResponse>(JsonOptions.Default);

        // Assert
        body!.Database.Should().Be("presets");
        body.Status.Should().BeOneOf("healthy", "unhealthy");
    }

    // ---------------------------------------------------------------------------
    // Shared response shapes
    // ---------------------------------------------------------------------------

    private sealed record HealthResponse(string Status);

    private sealed record DbHealthResponse(string Database, string DbType, string Status);
}
