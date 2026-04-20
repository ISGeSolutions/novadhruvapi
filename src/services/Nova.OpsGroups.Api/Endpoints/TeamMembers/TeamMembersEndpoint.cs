using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.OpsGroups.Api.Configuration;
using Nova.OpsGroups.Api.Endpoints;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.OpsGroups.Api.Endpoints.TeamMembers;

/// <summary>
/// Fetch ops team members: <c>POST /api/v1/grouptour-task-team-members</c>
///
/// Reads <c>user_security_rights</c> (role assignments) and <c>tenant_user_profile</c>
/// (display names) from AuthDb in a single join query.
/// Returns one object per user with a <c>roles[]</c> array — a user holding both
/// OPSMGR and OPSEXEC appears once with both roles listed.
/// Respects company_code / branch_code = 'XXXX' wildcard scope.
/// OPSMGR → "ops_manager" · OPSEXEC → "ops_exec" (mapped at the API layer).
/// </summary>
public static class TeamMembersEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/grouptour-task-team-members", HandleAsync)
             .RequireAuthorization()
             .WithName("GrouptourTaskTeamMembers");
    }

    private static async Task<IResult> HandleAsync(
        TeamMembersRequest       request,
        HttpContext              httpContext,
        IDbConnectionFactory     connectionFactory,
        IOptions<AuthDbSettings> authDbOptions,
        CancellationToken        ct)
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

        AuthDbSettings authDb = authDbOptions.Value;
        string         sql    = OpsGroupsDbHelper.TeamMembersQuerySql(authDb);

        IEnumerable<TeamMemberRow> rows;
        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(
                   authDb.ConnectionString, authDb.DbType))
        {
            rows = await conn.QueryAsync<TeamMemberRow>(
                sql,
                new { request.TenantId, request.CompanyCode, request.BranchCode },
                commandTimeout: 10);
        }

        var members = rows
            .GroupBy(r => r.UserId)
            .Select(g => new
            {
                user_id  = g.Key,
                name     = g.First().DisplayName,
                initials = ComputeInitials(g.First().DisplayName),
                roles    = g.Select(r => MapRoleCode(r.RoleCode)).OrderBy(r => r).ToList(),
            })
            .OrderBy(m => m.name)
            .ToList();

        return TypedResults.Ok(new { team_members = members });
    }

    private static string ComputeInitials(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant(),
        };
    }

    private static string MapRoleCode(string roleCode) => roleCode switch
    {
        "OPSMGR"  => "ops_manager",
        "OPSEXEC" => "ops_exec",
        _         => roleCode.ToLowerInvariant()
    };

    private sealed record TeamMemberRow(
        string UserId,
        string DisplayName,
        string RoleCode,
        string RoleFlags);

    private sealed record TeamMembersRequest : RequestContext;
}
