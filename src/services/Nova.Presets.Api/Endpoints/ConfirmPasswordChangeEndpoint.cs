using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.Presets.Api.Configuration;
using Nova.Shared.Data;

namespace Nova.Presets.Api.Endpoints;

/// <summary>
/// Confirm password change: <c>POST /api/v1/user-profile/confirm-password-change</c>
///
/// No Bearer token required — this is called when the user clicks the email link.
/// Looks up the pending request in PresetsDb by SHA-256 token hash,
/// applies the new password to <c>nova_auth.tenant_user_auth</c>,
/// then marks the request as confirmed.
/// </summary>
public static class ConfirmPasswordChangeEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/user-profile/confirm-password-change", HandleAsync)
             .AllowAnonymous()
             .WithName("ConfirmPasswordChange");
    }

    private static async Task<IResult> HandleAsync(
        ConfirmRequest               request,
        IDbConnectionFactory         connectionFactory,
        IOptions<AuthDbSettings>     authDbOptions,
        IOptions<PresetsDbSettings>  presetsDbOptions,
        CancellationToken            ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["token"] = ["token is required."] },
                title: "Validation failed");

        AuthDbSettings    authDb    = authDbOptions.Value;
        PresetsDbSettings presetsDb = presetsDbOptions.Value;

        ISqlDialect authDialect    = PresetsDbHelper.Dialect(authDb.DbType);
        ISqlDialect presetsDialect = PresetsDbHelper.Dialect(presetsDb.DbType);

        string userAuth  = authDialect.TableRef("nova_auth", "tenant_user_auth");
        string changeReq = presetsDialect.TableRef("presets", "tenant_password_change_requests");

        // URL-decode before hashing: the frontend decodes the query param automatically,
        // but Postman / manual callers may paste the URL-encoded form directly.
        string rawToken  = Uri.UnescapeDataString(request.Token);
        string tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        var    now       = PresetsDbHelper.UtcNow();

        // Step 1: Find valid pending request in PresetsDb
        PendingRequest? pending;
        using (IDbConnection presetsConn = connectionFactory.CreateFromConnectionString(
                   presetsDb.ConnectionString, presetsDb.DbType))
        {
            pending = await presetsConn.QuerySingleOrDefaultAsync<PendingRequest>(
                $"""
                SELECT id, tenant_id AS TenantId, user_id AS UserId, new_password_hash AS NewPasswordHash
                FROM   {changeReq}
                WHERE  token_hash    = @TokenHash
                AND    confirmed_on  IS NULL
                AND    expires_on    > @Now
                """,
                new { TokenHash = tokenHash, Now = now },
                commandTimeout: 10);
        }

        if (pending is null)
            return TypedResults.Problem(
                title:      "Bad request",
                detail:     "Invalid or expired confirmation token.",
                statusCode: StatusCodes.Status400BadRequest);

        // Step 2: Apply new password hash to nova_auth
        using (IDbConnection authConn = connectionFactory.CreateFromConnectionString(
                   authDb.ConnectionString, authDb.DbType))
        {
            await authConn.ExecuteAsync(
                $"""
                UPDATE {userAuth}
                SET    password_hash        = @Hash,
                       must_change_password = {authDialect.BooleanLiteral(false)},
                       updated_on           = @Now,
                       updated_by           = 'Auto',
                       updated_at           = 'Nova.Presets.Api'
                WHERE  tenant_id = @TenantId AND user_id = @UserId
                """,
                new { Hash = pending.NewPasswordHash, Now = now, pending.TenantId, pending.UserId },
                commandTimeout: 10);
        }

        // Step 3: Atomically mark request as confirmed — guards against two simultaneous requests
        // with the same token both passing the SELECT check above.
        using (IDbConnection presetsConn = connectionFactory.CreateFromConnectionString(
                   presetsDb.ConnectionString, presetsDb.DbType))
        {
            int confirmed = await presetsConn.ExecuteAsync(
                $"UPDATE {changeReq} SET confirmed_on = @Now WHERE id = @Id AND confirmed_on IS NULL",
                new { Now = now, pending.Id },
                commandTimeout: 10);

            if (confirmed == 0)
                return TypedResults.Problem(
                    title:      "Bad request",
                    detail:     "Invalid or expired confirmation token.",
                    statusCode: StatusCodes.Status400BadRequest);
        }

        return TypedResults.Ok(new { message = "Your password has been updated successfully." });
    }

    private sealed record PendingRequest(Guid Id, string TenantId, string UserId, string NewPasswordHash);

    private sealed record ConfirmRequest
    {
        public string? Token { get; init; }
    }
}
