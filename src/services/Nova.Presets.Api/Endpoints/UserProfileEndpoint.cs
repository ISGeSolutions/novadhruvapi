using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.Presets.Api.Configuration;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.Presets.Api.Endpoints;

/// <summary>
/// Fetch user profile: <c>POST /api/v1/user-profile</c>
///
/// Reads <c>tenant_user_profile</c> from AuthDb (name, email, avatar_url) and
/// <c>tenant_user_status</c> from PresetsDb (status_id, status_label, status_note).
/// Two separate queries — cross-DB join is not possible.
/// Defaults to <c>available / Available</c> if no status row exists.
/// </summary>
public static class UserProfileEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/user-profile", HandleAsync)
             .RequireAuthorization()
             .WithName("UserProfile");
    }

    private static async Task<IResult> HandleAsync(
        ProfileRequest               request,
        HttpContext                  httpContext,
        IDbConnectionFactory         connectionFactory,
        IOptions<AuthDbSettings>     authDbOptions,
        IOptions<PresetsDbSettings>  presetsDbOptions,
        CancellationToken            ct)
    {
        Dictionary<string, string[]> errors = RequestContextValidator.Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        string? jwtTenantId = httpContext.User.FindFirstValue("tenant_id");
        if (!string.Equals(request.TenantId, jwtTenantId, StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        AuthDbSettings    authDb    = authDbOptions.Value;
        PresetsDbSettings presetsDb = presetsDbOptions.Value;

        ISqlDialect authDialect    = PresetsDbHelper.Dialect(authDb.DbType);
        ISqlDialect presetsDialect = PresetsDbHelper.Dialect(presetsDb.DbType);

        string profileTable = authDialect.TableRef("nova_auth", "tenant_user_profile");
        string statusTable  = presetsDialect.TableRef("presets", "tenant_user_status");

        // Query 1 — AuthDb: name, email, avatar_url
        ProfileRow? profile;
        using (IDbConnection authConn = connectionFactory.CreateFromConnectionString(
                   authDb.ConnectionString, authDb.DbType))
        {
            profile = await authConn.QuerySingleOrDefaultAsync<ProfileRow>(
                $"""
                SELECT display_name AS DisplayName, email AS Email, avatar_url AS AvatarUrl
                FROM   {profileTable}
                WHERE  tenant_id = @TenantId
                AND    user_id   = @UserId
                AND    frz_ind   = {authDialect.BooleanLiteral(false)}
                """,
                new { request.TenantId, request.UserId },
                commandTimeout: 10);
        }

        if (profile is null)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     "User profile not found.",
                statusCode: StatusCodes.Status404NotFound);

        // Query 2 — PresetsDb: status (optional — COALESCE to defaults if missing)
        StatusRow? status;
        using (IDbConnection presetsConn = connectionFactory.CreateFromConnectionString(
                   presetsDb.ConnectionString, presetsDb.DbType))
        {
            status = await presetsConn.QuerySingleOrDefaultAsync<StatusRow>(
                $"""
                SELECT status_id AS StatusId, status_label AS StatusLabel, status_note AS StatusNote
                FROM   {statusTable}
                WHERE  tenant_id = @TenantId
                AND    user_id   = @UserId
                """,
                new { request.TenantId, request.UserId },
                commandTimeout: 10);
        }

        return TypedResults.Ok(new
        {
            user_id      = request.UserId,
            name         = profile.DisplayName,
            email        = profile.Email,
            avatar_url   = profile.AvatarUrl,
            status_id    = status?.StatusId    ?? "available",
            status_label = status?.StatusLabel ?? "Available",
            status_note  = status?.StatusNote,
        });
    }

    private sealed record ProfileRow(string DisplayName, string Email, string? AvatarUrl);
    private sealed record StatusRow(string StatusId, string StatusLabel, string? StatusNote);
    private sealed record ProfileRequest : RequestContext;
}
