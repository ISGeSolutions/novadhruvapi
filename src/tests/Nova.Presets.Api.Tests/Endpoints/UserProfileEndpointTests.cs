using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nova.Presets.Api.Tests.Helpers;

namespace Nova.Presets.Api.Tests.Endpoints;

/// <summary>
/// Tests for POST /api/v1/user-profile.
/// Authentication and tenant validation are exercised without a real database.
/// Happy-path tests (200 / 404 from DB queries) require a live database and are
/// not included here.
/// </summary>
public class UserProfileEndpointTests
{
    // Minimal valid request body.
    private static object ValidBody() => new
    {
        tenant_id  = TestConstants.TenantId,
        company_code = TestConstants.CompanyCode,
        branch_code  = TestConstants.BranchCode,
        user_id    = TestConstants.UserId,
    };

    // ---------------------------------------------------------------------------
    // Authentication
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_NoJwt_When_UserProfileIsCalled_Then_Returns401()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();
        // No Authorization header set.

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile", ValidBody());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------------------
    // Request validation — checked before any database access
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_MissingTenantId_When_UserProfileIsCalled_Then_Returns400()
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
        var response = await client.PostAsJsonAsync("/api/v1/user-profile", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Given_MissingUserId_When_UserProfileIsCalled_Then_Returns400()
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
        var response = await client.PostAsJsonAsync("/api/v1/user-profile", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------------------
    // Tenant isolation — JWT claim must match request body tenant_id
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_TenantIdMismatchBetweenJwtAndBody_When_UserProfileIsCalled_Then_Returns403()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();

        // JWT is issued for TenantId ("BTDK")
        var token = JwtFactory.CreateToken(
            tenantId: TestConstants.TenantId,
            userId:   TestConstants.UserId,
            secret:   TestConstants.JwtSecret,
            issuer:   TestConstants.JwtIssuer,
            audience: TestConstants.JwtAudience);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Body claims a different tenant
        var body = new
        {
            tenant_id  = "OTHERCO",
            company_code = TestConstants.CompanyCode,
            branch_code  = TestConstants.BranchCode,
            user_id    = TestConstants.UserId,
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
