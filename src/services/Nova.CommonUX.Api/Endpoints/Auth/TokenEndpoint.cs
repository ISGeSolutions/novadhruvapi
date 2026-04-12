using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.CommonUX.Api.Models;
using Nova.CommonUX.Api.Services;
using Nova.Shared.Data;

namespace Nova.CommonUX.Api.Endpoints.Auth;

/// <summary>
/// Machine-to-machine token: <c>POST /api/v1/auth/token</c>
/// Issues an application-level JWT using a tenant client secret (Argon2id-verified).
/// No Bearer token required — this is the bootstrap endpoint.
/// </summary>
public static class TokenEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/auth/token", HandleAsync)
             .AllowAnonymous()
             .WithName("AuthToken");
    }

    private static async Task<IResult> HandleAsync(
        TokenRequest                   request,
        IDbConnectionFactory           connectionFactory,
        IOptions<AuthDbSettings>       authDbOptions,
        ITokenService                  tokenService,
        CancellationToken              ct)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["tenant_id"] = ["tenant_id is required."] },
                title: "Validation failed");

        if (string.IsNullOrWhiteSpace(request.ClientSecret))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["client_secret"] = ["client_secret is required."] },
                title: "Validation failed");

        AuthDbSettings authDb  = authDbOptions.Value;
        ISqlDialect    dialect = AuthDbHelper.Dialect(authDb.DbType);
        string         secrets = dialect.TableRef("nova_auth", "tenant_secrets");

        using IDbConnection connection = connectionFactory.CreateFromConnectionString(
            authDb.ConnectionString, authDb.DbType);

        string? storedHash = await connection.ExecuteScalarAsync<string?>(
            $"SELECT client_secret_hash FROM {secrets} WHERE tenant_id = @TenantId AND frz_ind = {dialect.BooleanLiteral(false)}",
            new { request.TenantId },
            commandTimeout: 10);

        if (storedHash is null || !Argon2idHasher.Verify(request.ClientSecret, storedHash))
            return TypedResults.Problem(
                title:      "Unauthorized",
                detail:     "Invalid tenant_id or client_secret.",
                statusCode: StatusCodes.Status401Unauthorized);

        var (token, expiresIn) = tokenService.IssueJwt(request.TenantId, $"app:{request.TenantId}", []);
        return TypedResults.Ok(new AppTokenResponse(token, expiresIn));
    }

    private sealed record TokenRequest
    {
        public string TenantId     { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;
    }
}
