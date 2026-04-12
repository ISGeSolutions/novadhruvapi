using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Nova.Presets.Api.Tests.Helpers;

namespace Nova.Presets.Api.Tests.Endpoints;

/// <summary>
/// Tests for POST /api/v1/hello.
/// Endpoint is anonymous and stateless — no database, no JWT required.
/// </summary>
public class HelloWorldEndpointTests
{
    // Response contract — mirrors HelloWorldEndpoint's anonymous response object.
    // Declared here to keep the test file self-contained and to make the expected
    // wire shape explicit. snake_case binding is handled by JsonOptions.Default.
    private sealed record HelloWorldResponse(string Message);

    // ---------------------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_AnonymousRequest_When_HelloIsCalled_Then_Returns200()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.PostAsync("/api/v1/hello", content: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Given_AnonymousRequest_When_HelloIsCalled_Then_MessageIsPresetsApiRunning()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.PostAsync("/api/v1/hello", content: null);
        var body     = await response.Content.ReadFromJsonAsync<HelloWorldResponse>(JsonOptions.Default);

        // Assert
        body!.Message.Should().Be("Nova.Presets.Api is running.");
    }

    [Fact]
    public async Task Given_AnonymousRequest_When_HelloIsCalled_Then_ContentTypeIsJson()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.PostAsync("/api/v1/hello", content: null);

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    // ---------------------------------------------------------------------------
    // Wrong method
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_GetRequest_When_HelloEndpointIsCalled_Then_Returns405MethodNotAllowed()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/hello");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }
}
