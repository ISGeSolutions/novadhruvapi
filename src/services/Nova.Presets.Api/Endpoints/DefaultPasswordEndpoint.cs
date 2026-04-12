using System.Data;
using System.Globalization;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.Presets.Api.Configuration;
using Nova.Presets.Api.Services;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.Presets.Api.Endpoints;

/// <summary>
/// Set default password: <c>POST /api/v1/user/default-password</c>
///
/// Admin-only. Upserts <c>nova_auth.tenant_user_auth</c> with a default password
/// of <c>changeMe@ddMMM</c> (e.g. <c>changeMe@11Apr</c>), hashed via Argon2id.
/// Also sets <c>must_change_password = true</c>, clears <c>failed_login_count</c>
/// and <c>locked_until</c>.
///
/// The target user must already have a profile in <c>nova_auth.tenant_user_profile</c>.
/// </summary>
public static class DefaultPasswordEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/user/default-password", HandleAsync)
             .RequireAuthorization()
             .WithName("DefaultPassword");
    }

    private static async Task<IResult> HandleAsync(
        DefaultPasswordRequest       request,
        HttpContext                  httpContext,
        IDbConnectionFactory         connectionFactory,
        IOptions<AuthDbSettings>     authDbOptions,
        CancellationToken            ct)
    {
        Dictionary<string, string[]> errors = RequestContextValidator.Validate(request);

        if (string.IsNullOrWhiteSpace(request.TargetUserId))
            errors["target_user_id"] = ["target_user_id is required."];

        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        string? jwtTenantId = httpContext.User.FindFirstValue("tenant_id");
        if (!string.Equals(request.TenantId, jwtTenantId, StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        AuthDbSettings authDb   = authDbOptions.Value;
        ISqlDialect    dialect  = PresetsDbHelper.Dialect(authDb.DbType);
        string         profile  = dialect.TableRef("nova_auth", "tenant_user_profile");

        // Verify target user has a profile — prevent orphan auth rows
        bool profileExists;
        using (IDbConnection authConn = connectionFactory.CreateFromConnectionString(
                   authDb.ConnectionString, authDb.DbType))
        {
            profileExists = await authConn.ExecuteScalarAsync<bool>(
                $"""
                SELECT {dialect.BooleanLiteral(true)}
                FROM   {profile}
                WHERE  tenant_id = @TenantId
                AND    user_id   = @TargetUserId
                AND    frz_ind   = {dialect.BooleanLiteral(false)}
                """,
                new { request.TenantId, request.TargetUserId },
                commandTimeout: 10);
        }

        if (!profileExists)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     "Target user profile not found.",
                statusCode: StatusCodes.Status404NotFound);

        string defaultPassword = "changeMe@" + DateTime.UtcNow.ToString("ddMMM", CultureInfo.InvariantCulture);
        string passwordHash    = Argon2idHasher.Hash(defaultPassword);
        var    now             = PresetsDbHelper.UtcNow();

        using (IDbConnection authConn = connectionFactory.CreateFromConnectionString(
                   authDb.ConnectionString, authDb.DbType))
        {
            await authConn.ExecuteAsync(
                PresetsDbHelper.DefaultPasswordUpsertSql(authDb.DbType),
                new
                {
                    request.TenantId,
                    request.TargetUserId,
                    PasswordHash = passwordHash,
                    UpdatedBy    = request.UserId,
                    Now          = now,
                },
                commandTimeout: 10);
        }

        return TypedResults.Ok(new
        {
            message = "Default password set. User must change password on next login."
        });
    }

    private sealed record DefaultPasswordRequest : RequestContext
    {
        public string TargetUserId { get; init; } = string.Empty;
    }
}
