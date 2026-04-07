using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Nova.Shell.Api.Tests.Helpers;

namespace Nova.Shell.Api.Tests.Endpoints;

/// <summary>
/// Phase 1 tests for GET /api/v1/hello-world.
/// Endpoint is anonymous and stateless — no Redis, no database, no JWT required.
/// </summary>
public class HelloWorldEndpointTests
{
    // ---------------------------------------------------------------------------
    // Response contract — mirrors HelloWorldEndpoint's private response record.
    // Declared here to keep the test file self-contained and to make the expected
    // wire shape explicit. snake_case binding is handled by JsonOptions.Default.
    // ---------------------------------------------------------------------------
    private sealed record HelloWorldResponse(
        string         Message,
        DateTimeOffset Timestamp,
        string         CorrelationId,
        DateOnly       DepDate,
        DateTimeOffset CreatedOn);

    // ---------------------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_AnonymousRequest_When_HelloWorldIsCalled_Then_Returns200()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/hello-world");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Given_AnonymousRequest_When_HelloWorldIsCalled_Then_MessageIsHelloWorld()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/hello-world");
        var body     = await response.Content.ReadFromJsonAsync<HelloWorldResponse>(JsonOptions.Default);

        // Assert
        body!.Message.Should().Be("Hello, World!");
    }

    [Fact]
    public async Task Given_AnonymousRequest_When_HelloWorldIsCalled_Then_CorrelationIdIsPresentInResponse()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/hello-world");
        var body     = await response.Content.ReadFromJsonAsync<HelloWorldResponse>(JsonOptions.Default);

        // Assert
        // CorrelationIdMiddleware assigns an ID when none is provided by the caller.
        // An empty string means the middleware did not run or did not write to HttpContext.Items.
        body!.CorrelationId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Given_CallerSuppliesCorrelationId_When_HelloWorldIsCalled_Then_SameIdIsReturnedInResponse()
    {
        // Arrange
        var client        = TestHost.Create().CreateClient();
        var correlationId = Guid.NewGuid().ToString();
        var request       = new HttpRequestMessage(HttpMethod.Get, "/api/v1/hello-world");
        request.Headers.Add("X-Correlation-ID", correlationId);

        // Act
        var response = await client.SendAsync(request);
        var body     = await response.Content.ReadFromJsonAsync<HelloWorldResponse>(JsonOptions.Default);

        // Assert
        body!.CorrelationId.Should().Be(correlationId);
    }

    // ---------------------------------------------------------------------------
    // Date and time contract
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_AnonymousRequest_When_HelloWorldIsCalled_Then_DepDateIsDateOnlyWithNoTimeComponent()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/hello-world");
        var body     = await response.Content.ReadFromJsonAsync<HelloWorldResponse>(JsonOptions.Default);

        // Assert
        // DepDate represents a calendar date (departure, check-in).
        // It must deserialise as a DateOnly value — no time, no offset.
        body!.DepDate.Should().Be(new DateOnly(2026, 8, 15));
    }

    [Fact]
    public async Task Given_AnonymousRequest_When_HelloWorldIsCalled_Then_CreatedOnIsUtcOffset()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/hello-world");
        var body     = await response.Content.ReadFromJsonAsync<HelloWorldResponse>(JsonOptions.Default);

        // Assert
        // CreatedOn must be a UTC point-in-time (DateTimeOffset with zero offset).
        // A non-zero offset means the server returned a local time — which is wrong.
        body!.CreatedOn.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Given_AnonymousRequest_When_HelloWorldIsCalled_Then_TimestampIsRecentUtc()
    {
        // Arrange
        // Truncate to the second: the server's UtcDateTimeOffsetConverter drops sub-second precision.
        // A before value with sub-second fractional part would be greater than the truncated response
        // timestamp even when the server generated the timestamp after before was captured.
        var before = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/hello-world");
        var after    = DateTimeOffset.UtcNow;
        var body     = await response.Content.ReadFromJsonAsync<HelloWorldResponse>(JsonOptions.Default);

        // Assert
        body!.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        body.Timestamp.Offset.Should().Be(TimeSpan.Zero);
    }

    // ---------------------------------------------------------------------------
    // Wire format
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_AnonymousRequest_When_HelloWorldIsCalled_Then_ContentTypeIsJson()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/hello-world");

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    // ---------------------------------------------------------------------------
    // Wrong method
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_PostRequest_When_HelloWorldEndpointIsCalled_Then_Returns405MethodNotAllowed()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.PostAsync("/api/v1/hello-world", content: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }
}
