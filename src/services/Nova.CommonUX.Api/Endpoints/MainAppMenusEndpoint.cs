using System.Data;
using System.IdentityModel.Tokens.Jwt;
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
/// Returns the role-filtered, tree-assembled menu for the current user.
/// <list type="bullet">
///   <item>Role filtering: server-side — only items the user can see are returned.</item>
///   <item>Tree assembly: server-side — children nested under their parent.</item>
///   <item>URL template resolution: server-side — <c>{tenant_id}</c> and <c>{user_id}</c> tokens replaced.</item>
/// </list>
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
        MenusRequest                 request,
        HttpContext                  httpContext,
        IDbConnectionFactory         connectionFactory,
        IOptions<AuthDbSettings>     authDbOptions,
        CancellationToken            ct)
    {
        Dictionary<string, string[]> contextErrors = Nova.Shared.Validation.RequestContextValidator.Validate(request);
        if (contextErrors.Count > 0)
            return TypedResults.ValidationProblem(contextErrors, title: "Validation failed");

        string? jwtTenantId = httpContext.User.FindFirstValue("tenant_id");
        if (!string.Equals(request.TenantId, jwtTenantId, StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        // Collect user roles from JWT claims
        HashSet<string> userRoles = httpContext.User
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        AuthDbSettings authDb  = authDbOptions.Value;
        ISqlDialect    dialect = CreateDialect(authDb.DbType);
        string         menus   = dialect.TableRef("nova_auth", "tenant_menu_items");

        using IDbConnection connection = connectionFactory.CreateFromConnectionString(
            authDb.ConnectionString, authDb.DbType);

        IEnumerable<MenuRow> rows = await connection.QueryAsync<MenuRow>(
            $"""
            SELECT menu_item_id, parent_id, label, icon, route,
                   external_url_template, external_url_param_mode, required_roles, sort_order
            FROM {menus}
            WHERE tenant_id = @TenantId AND is_active = {dialect.BooleanLiteral(true)}
              AND frz_ind = {dialect.BooleanLiteral(false)}
            ORDER BY sort_order
            """,
            new { request.TenantId },
            commandTimeout: 10);

        string userId = request.UserId;

        // Role filtering + URL template resolution
        var visibleItems = rows
            .Where(r => IsVisible(r, userRoles))
            .Select(r => ResolveUrls(r, request.TenantId, userId))
            .ToList();

        // Tree assembly
        List<MenuItemDto> tree = AssembleTree(visibleItems);

        return TypedResults.Ok(tree);
    }

    private static bool IsVisible(MenuRow row, HashSet<string> userRoles)
    {
        if (string.IsNullOrWhiteSpace(row.RequiredRoles)) return true;
        string[] required = row.RequiredRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return required.Any(r => userRoles.Contains(r));
    }

    private static MenuRow ResolveUrls(MenuRow row, string tenantId, string userId)
    {
        if (string.IsNullOrEmpty(row.ExternalUrlTemplate)) return row;
        string resolved = row.ExternalUrlTemplate
            .Replace("{tenant_id}", tenantId)
            .Replace("{user_id}", userId);
        return row with { ExternalUrlTemplate = resolved };
    }

    private static List<MenuItemDto> AssembleTree(List<MenuRow> items)
    {
        // Build lookup by id
        Dictionary<string, MenuItemDto> byId = items.ToDictionary(
            r => r.MenuItemId,
            r => new MenuItemDto
            {
                Id                   = r.MenuItemId,
                Label                = r.Label,
                Icon                 = r.Icon,
                Route                = r.Route,
                ExternalUrl          = r.ExternalUrlTemplate,
                ExternalUrlParamMode = r.ExternalUrlParamMode ?? "none",
            });

        var roots = new List<MenuItemDto>();

        foreach (MenuRow item in items)
        {
            MenuItemDto dto = byId[item.MenuItemId];
            if (string.IsNullOrEmpty(item.ParentId))
            {
                roots.Add(dto);
            }
            else if (byId.TryGetValue(item.ParentId, out MenuItemDto? parent))
            {
                parent.Children ??= [];
                parent.Children.Add(dto);
            }
        }

        return roots;
    }

    private static ISqlDialect CreateDialect(Nova.Shared.Data.DbType dbType) => dbType switch
    {
        Nova.Shared.Data.DbType.Postgres => new PostgresDialect(),
        Nova.Shared.Data.DbType.MariaDb  => new MariaDbDialect(),
        _                                 => new MsSqlDialect()
    };

    private sealed record MenuRow(
        string  MenuItemId,
        string? ParentId,
        string  Label,
        string? Icon,
        string? Route,
        string? ExternalUrlTemplate,
        string? ExternalUrlParamMode,
        string? RequiredRoles,
        int     SortOrder)
    {
        public string? ExternalUrlTemplate { get; set; } = ExternalUrlTemplate;
    }

    private sealed class MenuItemDto
    {
        public string          Id                   { get; init; } = string.Empty;
        public string          Label                { get; init; } = string.Empty;
        public string?         Icon                 { get; init; }
        public string?         Route                { get; init; }
        public string?         ExternalUrl          { get; init; }
        public string          ExternalUrlParamMode { get; init; } = "none";
        public List<MenuItemDto>? Children          { get; set; }
    }

    private sealed record MenusRequest : RequestContext;
}
