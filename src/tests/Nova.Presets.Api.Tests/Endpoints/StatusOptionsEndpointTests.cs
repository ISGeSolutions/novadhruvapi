using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nova.Presets.Api.Tests.Helpers;

namespace Nova.Presets.Api.Tests.Endpoints;

/// <summary>
/// Tests for POST /api/v1/user-profile/status-options.
/// Authentication is required. No database access — the fixed status list is defined
/// in <c>UserStatusOptions</c> and held in memory.
/// </summary>
public class StatusOptionsEndpointTests
{
    // Response contract — mirrors the anonymous projection in StatusOptionsEndpoint.
    private sealed record StatusOptionResponse(string Id, string Label, string Colour);

    // Minimal valid request body — all four RequestContext fields are required.
    private static object ValidBody() => new
    {
        tenant_id  = TestConstants.TenantId,
        company_code = TestConstants.CompanyCode,
        branch_code  = TestConstants.BranchCode,
        user_id    = TestConstants.UserId,
    };

    // ---------------------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_ValidJwt_When_StatusOptionsIsCalled_Then_Returns200()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();
        var token  = JwtFactory.CreateToken(
            tenantId: TestConstants.TenantId,
            userId:   TestConstants.UserId,
            secret:   TestConstants.JwtSecret,
            issuer:   TestConstants.JwtIssuer,
            audience: TestConstants.JwtAudience);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile/status-options", ValidBody());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Given_ValidJwt_When_StatusOptionsIsCalled_Then_ReturnsAllFiveOptions()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();
        var token  = JwtFactory.CreateToken(
            tenantId: TestConstants.TenantId,
            userId:   TestConstants.UserId,
            secret:   TestConstants.JwtSecret,
            issuer:   TestConstants.JwtIssuer,
            audience: TestConstants.JwtAudience);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile/status-options", ValidBody());
        var body     = await response.Content.ReadFromJsonAsync<List<StatusOptionResponse>>(JsonOptions.Default);

        // Assert
        body!.Should().HaveCount(5);
    }

    [Fact]
    public async Task Given_ValidJwt_When_StatusOptionsIsCalled_Then_AllOptionsHaveNonEmptyIdLabelAndColour()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();
        var token  = JwtFactory.CreateToken(
            tenantId: TestConstants.TenantId,
            userId:   TestConstants.UserId,
            secret:   TestConstants.JwtSecret,
            issuer:   TestConstants.JwtIssuer,
            audience: TestConstants.JwtAudience);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile/status-options", ValidBody());
        var body     = await response.Content.ReadFromJsonAsync<List<StatusOptionResponse>>(JsonOptions.Default);

        // Assert
        body!.Should().AllSatisfy(opt =>
        {
            opt.Id.Should().NotBeNullOrWhiteSpace();
            opt.Label.Should().NotBeNullOrWhiteSpace();
            // Colour is a CSS hex code — must start with '#' and be 7 characters
            opt.Colour.Should().MatchRegex(@"^#[0-9a-fA-F]{6}$");
        });
    }

    [Fact]
    public async Task Given_ValidJwt_When_StatusOptionsIsCalled_Then_AvailableOptionIsPresent()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();
        var token  = JwtFactory.CreateToken(
            tenantId: TestConstants.TenantId,
            userId:   TestConstants.UserId,
            secret:   TestConstants.JwtSecret,
            issuer:   TestConstants.JwtIssuer,
            audience: TestConstants.JwtAudience);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile/status-options", ValidBody());
        var body     = await response.Content.ReadFromJsonAsync<List<StatusOptionResponse>>(JsonOptions.Default);

        // Assert
        // "available" must be present — it is the default status for users with no explicit status row.
        body!.Should().Contain(opt => opt.Id == "available" && opt.Label == "Available");
    }

    // ---------------------------------------------------------------------------
    // Authentication
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_NoJwt_When_StatusOptionsIsCalled_Then_Returns401()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();
        // No Authorization header set.

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile/status-options", ValidBody());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------------------
    // Request validation
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_MissingTenantId_When_StatusOptionsIsCalled_Then_Returns400()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();
        var token  = JwtFactory.CreateToken(
            tenantId: TestConstants.TenantId,
            userId:   TestConstants.UserId,
            secret:   TestConstants.JwtSecret,
            issuer:   TestConstants.JwtIssuer,
            audience: TestConstants.JwtAudience);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new
        {
            // tenant_id omitted
            company_code = TestConstants.CompanyCode,
            branch_code  = TestConstants.BranchCode,
            user_id    = TestConstants.UserId,
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile/status-options", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Given_MissingUserId_When_StatusOptionsIsCalled_Then_Returns400()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();
        var token  = JwtFactory.CreateToken(
            tenantId: TestConstants.TenantId,
            userId:   TestConstants.UserId,
            secret:   TestConstants.JwtSecret,
            issuer:   TestConstants.JwtIssuer,
            audience: TestConstants.JwtAudience);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new
        {
            tenant_id  = TestConstants.TenantId,
            company_code = TestConstants.CompanyCode,
            branch_code  = TestConstants.BranchCode,
            // user_id omitted
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile/status-options", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
