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
/// Users by role: <c>POST /api/v1/users/by-role</c>
///
/// Returns ops team members filtered by role(s) and optionally scoped to given branches.
/// One object per user with a <c>roles[]</c> array — a user holding both OPSMGR and OPSEXEC
/// appears once with both roles listed. Respects company_code / branch_code = 'XXXX' wildcard.
/// Relocated from Nova.OpsGroups.Api with added role and branch filters.
/// </summary>
public static class UsersByRoleEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/users/by-role", HandleAsync)
             .RequireAuthorization()
             .WithName("UsersByRole");
    }

    private static async Task<IResult> HandleAsync(
        UsersByRoleRequest       request,
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

        string[] roleCodes = MapRolesToCodes(request.Roles);
        string   sql       = PresetsDbHelper.UsersByRoleQuerySql(authDb, roleCodes, request.BranchCodeFilter);

        IEnumerable<UserRoleRow> rows;
        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(
                   authDb.ConnectionString, authDb.DbType))
        {
            rows = await conn.QueryAsync<UserRoleRow>(
                sql,
                new
                {
                    request.TenantId,
                    request.CompanyCode,
                    RoleCodes  = roleCodes,
                    BranchFilter = request.BranchCodeFilter ?? [],
                },
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

    private static string[] MapRolesToCodes(string[]? roles)
    {
        if (roles is null || roles.Length == 0)
            return ["OPSMGR", "OPSEXEC"];

        return roles.Select(r => r switch
        {
            "ops_manager" => "OPSMGR",
            "ops_exec"    => "OPSEXEC",
            _             => r.ToUpperInvariant(),
        }).ToArray();
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
        _         => roleCode.ToLowerInvariant(),
    };

    private sealed record UserRoleRow(string UserId, string DisplayName, string RoleCode);

    private sealed record UsersByRoleRequest : RequestContext
    {
        public string[]? Roles            { get; set; }
        public string[]? BranchCodeFilter { get; set; }
    }
}
