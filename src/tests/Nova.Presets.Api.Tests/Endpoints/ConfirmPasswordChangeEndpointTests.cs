using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Nova.Presets.Api.Tests.Helpers;

namespace Nova.Presets.Api.Tests.Endpoints;

/// <summary>
/// Tests for POST /api/v1/user-profile/confirm-password-change.
/// This endpoint is anonymous — no Bearer token is required. It is called when the
/// user clicks the confirmation link in their email.
/// Token validation (not-expired, not-already-confirmed) requires a live database
/// and is not tested here. Only the missing-token validation case is covered.
/// </summary>
public class ConfirmPasswordChangeEndpointTests
{
    // ---------------------------------------------------------------------------
    // Request validation — checked before any database access
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_MissingToken_When_ConfirmPasswordChangeIsCalled_Then_Returns400()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();
        // No Authorization header needed — endpoint is AllowAnonymous.

        var body = new
        {
            // token omitted
        };

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/v1/user-profile/confirm-password-change",
            body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Given_AnonymousRequest_When_ConfirmPasswordChangeEndpointIsPresent_Then_DoesNotReturn404()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        var body = new { token = "any-token-value" };

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/v1/user-profile/confirm-password-change",
            body);

        // Assert
        // A 404 would mean the route is not wired. The endpoint should be reachable —
        // it will return 400 (invalid/expired token from DB lookup) but not 404.
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }
}
