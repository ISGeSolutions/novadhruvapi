using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.CommonUX.Api.Configuration;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.CommonUX.Api.Endpoints;

/// <summary>
/// Navigation menus: <c>POST /api/v1/novadhruv-mainapp-menus</c>
/// Returns the tree-assembled menu for the current user.
/// <list type="bullet">
///   <item>Root node: determined by <c>program_id_root</c> on the user's profile.</item>
///   <item>Tree assembly: server-side — children nested under their parent.</item>
///   <item>URL token resolution: server-side — <c>{tenant_id}</c> and <c>{user_id}</c> replaced.</item>
/// </list>
/// Programs and program_tree live in the presets schema/database, queried via
/// cross-DB reference from the auth connection (same server instance).
/// </summary>
public static class MainAppMenusEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/novadhruv-mainapp-menus", HandleAsync)
             .RequireAuthorization()
             .WithName("MainAppMenus");
    }

    private static async Task<IResult> HandleAsync(
        MenusRequest             request,
        HttpContext              httpContext,
        IDbConnectionFactory     connectionFactory,
        IOptions<AuthDbSettings> authDbOptions,
        CancellationToken        ct)
    {
        Dictionary<string, string[]> contextErrors = RequestContextValidator.Validate(request);
        if (contextErrors.Count > 0)
            return TypedResults.ValidationProblem(contextErrors, title: "Validation failed");

        string? jwtTenantId = httpContext.User.FindFirstValue("tenant_id");
        if (!string.Equals(request.TenantId, jwtTenantId, StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        AuthDbSettings authDb  = authDbOptions.Value;
        ISqlDialect    dialect = CreateDialect(authDb.DbType);

        string userProfile = dialect.TableRef("nova_auth", "tenant_user_profile");
        string programs    = dialect.TableRef("presets",   "programs");
        string programTree = dialect.TableRef("presets",   "program_tree");

        using IDbConnection connection = connectionFactory.CreateFromConnectionString(
            authDb.ConnectionString, authDb.DbType);

        // Look up the user's menu root
        string? programIdRoot = await connection.QuerySingleOrDefaultAsync<string?>(
            $"""
            SELECT program_id_root
            FROM   {userProfile}
            WHERE  tenant_id = @TenantId
            AND    user_id   = @UserId
            AND    frz_ind   = {dialect.BooleanLiteral(false)}
            """,
            new { request.TenantId, request.UserId },
            commandTimeout: 10);

        if (programIdRoot is null)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     "User profile not found or program root not assigned.",
                statusCode: StatusCodes.Status404NotFound);

        // Fetch all active program nodes
        IEnumerable<ProgramRow> programRows = await connection.QueryAsync<ProgramRow>(
            $"""
            SELECT id, name, nav_type, route, external_url, external_url_param_mode, icon
            FROM   {programs}
            WHERE  is_active = {dialect.BooleanLiteral(true)}
            AND    frz_ind   = {dialect.BooleanLiteral(false)}
            """,
            commandTimeout: 10);

        // Fetch all active tree entries
        IEnumerable<TreeRow> treeRows = await connection.QueryAsync<TreeRow>(
            $"""
            SELECT program_id_parent, program_id_child, sort_order
            FROM   {programTree}
            WHERE  frz_ind = {dialect.BooleanLiteral(false)}
            ORDER  BY program_id_parent, sort_order
            """,
            commandTimeout: 10);

        string userId = request.UserId;

        var programsById = programRows
            .ToDictionary(p => p.Id);

        var childrenOf = treeRows
            .GroupBy(t => t.ProgramIdParent)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(t => t.SortOrder).Select(t => t.ProgramIdChild).ToList());

        List<MenuItemDto> tree = BuildChildren(programIdRoot, programsById, childrenOf,
                                               request.TenantId, userId);

        return TypedResults.Ok(tree);
    }

    private static List<MenuItemDto> BuildChildren(
        string                         parentId,
        Dictionary<string, ProgramRow> programsById,
        Dictionary<string, List<string>> childrenOf,
        string                         tenantId,
        string                         userId)
    {
        if (!childrenOf.TryGetValue(parentId, out List<string>? childIds)) return [];

        var result = new List<MenuItemDto>();
        foreach (string childId in childIds)
        {
            if (!programsById.TryGetValue(childId, out ProgramRow? prog)) continue;

            string? resolvedExternalUrl = ResolveUrl(prog.ExternalUrl, tenantId, userId);

            var dto = new MenuItemDto
            {
                Id                   = prog.Id,
                Label                = prog.Name,
                Icon                 = prog.Icon,
                Route                = prog.Route,
                ExternalUrl          = resolvedExternalUrl,
                ExternalUrlParamMode = prog.ExternalUrlParamMode ?? "none",
            };

            if (prog.NavType == "group")
                dto.Children = BuildChildren(childId, programsById, childrenOf, tenantId, userId);

            result.Add(dto);
        }
        return result;
    }

    private static string? ResolveUrl(string? url, string tenantId, string userId)
    {
        if (string.IsNullOrEmpty(url)) return url;
        return url
            .Replace("{tenant_id}", tenantId)
            .Replace("{user_id}",   userId);
    }

    private static ISqlDialect CreateDialect(Nova.Shared.Data.DbType dbType) => dbType switch
    {
        Nova.Shared.Data.DbType.Postgres => new PostgresDialect(),
        Nova.Shared.Data.DbType.MariaDb  => new MariaDbDialect(),
        _                                 => new MsSqlDialect()
    };

    private sealed record ProgramRow(
        string  Id,
        string  Name,
        string  NavType,
        string? Route,
        string? ExternalUrl,
        string? ExternalUrlParamMode,
        string? Icon);

    private sealed record TreeRow(
        string ProgramIdParent,
        string ProgramIdChild,
        int    SortOrder);

    private sealed class MenuItemDto
    {
        public string             Id                   { get; init; } = string.Empty;
        public string             Label                { get; init; } = string.Empty;
        public string?            Icon                 { get; init; }
        public string?            Route                { get; init; }
        public string?            ExternalUrl          { get; init; }
        public string             ExternalUrlParamMode { get; init; } = "none";
        public List<MenuItemDto>? Children             { get; set; }
    }

    private sealed record MenusRequest : RequestContext;
}
