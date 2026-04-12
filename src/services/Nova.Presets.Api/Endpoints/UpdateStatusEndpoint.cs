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
/// Update presence status: <c>PATCH /api/v1/user-profile/status</c>
///
/// Validates that the submitted <c>status_id</c> is an active option for the
/// caller's tenant/company/branch, then upserts <c>tenant_user_status</c> in
/// PresetsDb using dialect-specific SQL (MERGE / ON CONFLICT / ON DUPLICATE KEY).
/// Returns the full profile on success.
/// </summary>
public static class UpdateStatusEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapMethods("/user-profile/status", ["PATCH"], HandleAsync)
             .RequireAuthorization()
             .WithName("UpdateStatus");
    }

    private static async Task<IResult> HandleAsync(
        UpdateStatusRequest          request,
        HttpContext                  httpContext,
        IDbConnectionFactory         connectionFactory,
        IOptions<AuthDbSettings>     authDbOptions,
        IOptions<PresetsDbSettings>  presetsDbOptions,
        CancellationToken            ct)
    {
        Dictionary<string, string[]> errors = RequestContextValidator.Validate(request);

        if (string.IsNullOrWhiteSpace(request.StatusId))
            errors["status_id"] = ["status_id is required."];

        if (request.StatusNote?.Length > 200)
            errors["status_note"] = ["status_note must be 200 characters or fewer."];

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

        // Validate status_id against tenant/company/branch-scoped options
        StatusOptionRow? option;
        using (IDbConnection presetsConn = connectionFactory.CreateFromConnectionString(
                   presetsDb.ConnectionString, presetsDb.DbType))
        {
            option = await presetsConn.QuerySingleOrDefaultAsync<StatusOptionRow>(
                PresetsDbHelper.FindStatusOptionSql(presetsDb),
                new { request.TenantId, request.CompanyCode, request.BranchCode,
                      StatusCode = request.StatusId },
                commandTimeout: 10);
        }

        if (option is null)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["status_id"] = [$"'{request.StatusId}' is not a valid status option."]
                },
                title: "Validation failed");

        // Upsert in PresetsDb
        using (IDbConnection presetsConn = connectionFactory.CreateFromConnectionString(
                   presetsDb.ConnectionString, presetsDb.DbType))
        {
            await presetsConn.ExecuteAsync(
                PresetsDbHelper.StatusUpsertSql(presetsDb),
                new
                {
                    request.TenantId,
                    request.UserId,
                    StatusId    = option.StatusCode,
                    StatusLabel = option.Label,
                    StatusNote  = request.StatusNote,
                    Now         = PresetsDbHelper.UtcNow(),
                },
                commandTimeout: 10);
        }

        // Re-fetch profile from AuthDb for the response
        ISqlDialect authDialect  = PresetsDbHelper.Dialect(authDb.DbType);
        string      profileTable = authDialect.TableRef("nova_auth", "tenant_user_profile");

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

        return TypedResults.Ok(new
        {
            user_id      = request.UserId,
            name         = profile.DisplayName,
            email        = profile.Email,
            avatar_url   = profile.AvatarUrl,
            status_id    = option.StatusCode,
            status_label = option.Label,
            status_note  = request.StatusNote,
        });
    }

    private sealed record ProfileRow(string DisplayName, string Email, string? AvatarUrl);
    private sealed record StatusOptionRow(string StatusCode, string Label, string Colour);

    private sealed record UpdateStatusRequest : RequestContext
    {
        public string? StatusId   { get; init; }
        public string? StatusNote { get; init; }
    }
}
