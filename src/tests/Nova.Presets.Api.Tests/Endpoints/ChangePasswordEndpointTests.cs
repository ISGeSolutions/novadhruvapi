using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nova.Presets.Api.Tests.Helpers;

namespace Nova.Presets.Api.Tests.Endpoints;

/// <summary>
/// Tests for POST /api/v1/user-profile/change-password.
/// Authentication, tenant validation, and request field validation (including password
/// policy) are exercised without a real database. Happy-path tests (current password
/// verification + DB write + email dispatch) require a live database and are not
/// included here.
/// </summary>
public class ChangePasswordEndpointTests
{
    // Minimal valid-shaped request body. The current password will not be verified
    // against a real DB row in these tests — they all fail before the DB is reached.
    private static object ValidBody() => new
    {
        tenant_id        = TestConstants.TenantId,
        company_code       = TestConstants.CompanyCode,
        branch_code        = TestConstants.BranchCode,
        user_id          = TestConstants.UserId,
        current_password = "OldPassword1",
        new_password     = "NewPassword1",
    };

    // ---------------------------------------------------------------------------
    // Authentication
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_NoJwt_When_ChangePasswordIsCalled_Then_Returns401()
    {
        // Arrange
        var client = TestHost.Create().CreateClient();
        // No Authorization header set.

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile/change-password", ValidBody());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------------------
    // Request validation — checked before any database access
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_MissingCurrentPassword_When_ChangePasswordIsCalled_Then_Returns400()
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
            tenant_id    = TestConstants.TenantId,
            company_code   = TestConstants.CompanyCode,
            branch_code    = TestConstants.BranchCode,
            user_id      = TestConstants.UserId,
            // current_password omitted
            new_password = "NewPassword1",
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile/change-password", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Given_MissingNewPassword_When_ChangePasswordIsCalled_Then_Returns400()
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
            tenant_id        = TestConstants.TenantId,
            company_code       = TestConstants.CompanyCode,
            branch_code        = TestConstants.BranchCode,
            user_id          = TestConstants.UserId,
            current_password = "OldPassword1",
            // new_password omitted
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile/change-password", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Given_NewPasswordFailsPolicy_When_ChangePasswordIsCalled_Then_Returns400()
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
            tenant_id        = TestConstants.TenantId,
            company_code       = TestConstants.CompanyCode,
            branch_code        = TestConstants.BranchCode,
            user_id          = TestConstants.UserId,
            current_password = "OldPassword1",
            new_password     = "short",  // too short, no uppercase, no digit
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile/change-password", body);

        // Assert
        // Password policy requires: minimum 8 characters, at least one uppercase letter,
        // one lowercase letter, and one digit.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Given_NewPasswordNoUppercase_When_ChangePasswordIsCalled_Then_Returns400()
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
            tenant_id        = TestConstants.TenantId,
            company_code       = TestConstants.CompanyCode,
            branch_code        = TestConstants.BranchCode,
            user_id          = TestConstants.UserId,
            current_password = "OldPassword1",
            new_password     = "nouppercase1",  // no uppercase letter
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile/change-password", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Given_MissingTenantId_When_ChangePasswordIsCalled_Then_Returns400()
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
            company_code       = TestConstants.CompanyCode,
            branch_code        = TestConstants.BranchCode,
            user_id          = TestConstants.UserId,
            current_password = "OldPassword1",
            new_password     = "NewPassword1",
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile/change-password", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------------------
    // Tenant isolation — JWT claim must match request body tenant_id
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Given_TenantIdMismatchBetweenJwtAndBody_When_ChangePasswordIsCalled_Then_Returns403()
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
            tenant_id        = "OTHERCO",
            company_code       = TestConstants.CompanyCode,
            branch_code        = TestConstants.BranchCode,
            user_id          = TestConstants.UserId,
            current_password = "OldPassword1",
            new_password     = "NewPassword1",
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/user-profile/change-password", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
