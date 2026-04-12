using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nova.Presets.Api.Tests.Helpers;

namespace Nova.Presets.Api.Tests.Endpoints;

/// <summary>
/// Tests for PATCH /api/v1/user-profile/status.
/// Authentication, tenant validation, and request field validation are all exercised
/// without a real database. Happy-path tests (200 upsert + re-fetched profile) require
/// a live database and are not included here.
/// </summary>
public class UpdateStatusEndpointTests
{
    // Minimal valid request body with a known-good status_id.
    private static object ValidBody(string statusId = "available") => new
    {
        tenant_id  = TestConstants.TenantId,
        company_code = TestConstants.CompanyCode,
        branch_code  = TestConstants.BranchCode,
        user_id    = TestConstants.UserId,
        status_id  = statusId,
    };

    private static HttpContent JsonBody(object body) =>
        JsonContent.Create(body);

    // ---------------------------------------------------------------------------
    // Authentication
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_NoJwt_When_UpdateStatusIsCalled_Then_Returns401()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();
        // No Authorization header set.

        // Act
        var response = await client.PatchAsync(
            "/api/v1/user-profile/status",
            JsonBody(ValidBody()));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------------------
    // Request validation — checked before any database access
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_MissingStatusId_When_UpdateStatusIsCalled_Then_Returns400()
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
            user_id    = TestConstants.UserId,
            // status_id omitted
        };

        // Act
        var response = await client.PatchAsync("/api/v1/user-profile/status", JsonBody(body));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Given_InvalidStatusId_When_UpdateStatusIsCalled_Then_Returns400()
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
        var response = await client.PatchAsync(
            "/api/v1/user-profile/status",
            JsonBody(ValidBody(statusId: "not-a-real-status")));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Given_StatusNoteTooLong_When_UpdateStatusIsCalled_Then_Returns400()
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
            tenant_id   = TestConstants.TenantId,
            company_code  = TestConstants.CompanyCode,
            branch_code   = TestConstants.BranchCode,
            user_id     = TestConstants.UserId,
            status_id   = "available",
            status_note = new string('x', 201), // one character over the 200-character limit
        };

        // Act
        var response = await client.PatchAsync("/api/v1/user-profile/status", JsonBody(body));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Given_MissingTenantId_When_UpdateStatusIsCalled_Then_Returns400()
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
            status_id  = "available",
        };

        // Act
        var response = await client.PatchAsync("/api/v1/user-profile/status", JsonBody(body));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------------------
    // Tenant isolation — JWT claim must match request body tenant_id
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_TenantIdMismatchBetweenJwtAndBody_When_UpdateStatusIsCalled_Then_Returns403()
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
            status_id  = "available",
        };

        // Act
        var response = await client.PatchAsync("/api/v1/user-profile/status", JsonBody(body));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
