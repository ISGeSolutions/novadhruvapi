using System.Security.Claims;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.OpsGroups.Api.Endpoints;

/// <summary>
/// Page-level configuration: <c>POST /api/v1/page-config</c>
/// Returns feature-flag values for the requested page without a frontend redeploy.
/// Values are read from appsettings — no DB call.
/// </summary>
public static class PageConfigEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/page-config", HandleAsync)
             .RequireAuthorization()
             .WithName("PageConfig");
    }

    private static IResult HandleAsync(
        PageConfigRequest request,
        HttpContext        httpContext,
        IConfiguration     configuration,
        CancellationToken  ct)
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

        int  pastDepartureYearFloor = configuration.GetValue("PageConfig:PastDepartureYearFloor", 2024);
        int  auditPageSizeDefault   = configuration.GetValue("PageConfig:AuditPageSizeDefault",   50);
        bool fuseSearchEnabled      = configuration.GetValue("PageConfig:FuseSearchEnabled",      false);

        return TypedResults.Ok(new
        {
            config = new
            {
                past_departure_year_floor = pastDepartureYearFloor,
                audit_page_size_default   = auditPageSizeDefault,
                fuse_search_enabled       = fuseSearchEnabled,
            }
        });
    }

    private sealed record PageConfigRequest : RequestContext
    {
        public string? PageKey { get; init; }
    }
}
