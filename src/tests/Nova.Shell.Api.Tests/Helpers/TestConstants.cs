namespace Nova.Shell.Api.Tests.Helpers;

/// <summary>
/// All fixed values used across the test suite.
/// Never hardcode these strings directly in test methods — always reference this class.
/// </summary>
public static class TestConstants
{
    // JWT — values must match appsettings.json Jwt section.
    // PassthroughCipherService returns SecretKey as-is, so this is plaintext.
    public const string JwtSecret   = "nova-test-signing-key-minimum-32-chars-x";
    public const string JwtIssuer   = "https://auth.nova.internal";
    public const string JwtAudience = "nova-api";

    // Internal service-to-service auth
    public const string InternalJwtSecret   = "nova-internal-test-key-minimum-32-chars";
    public const string InternalJwtIssuer   = "nova-shell-test";
    public const string InternalJwtAudience = "nova-internal";

    // Tenancy — must match a tenant entry in appsettings.json
    public const string TenantId = "BLDK";
    public const string UserId   = "test-user-001";
}
