using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.OpsGroups.Api.Configuration;
using Nova.OpsGroups.Api.Endpoints;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.OpsGroups.Api.Endpoints.SlaRules;

public static class SlaRulesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/grouptour-task-sla-rules",   HandleFetchAsync)
             .RequireAuthorization()
             .WithName("SlaRulesFetch");

        group.MapPatch("/grouptour-task-sla-rules",  HandleSaveAsync)
             .RequireAuthorization()
             .WithName("SlaRulesSave");
    }

    // -------------------------------------------------------------------------
    // Fetch
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleFetchAsync(
        FetchRequest                     request,
        HttpContext                      httpContext,
        IDbConnectionFactory             connectionFactory,
        IOptions<OpsGroupsDbSettings>    opsGroupsDbOptions,
        CancellationToken                ct)
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

        OpsGroupsDbSettings db = opsGroupsDbOptions.Value;

        IEnumerable<SlaRuleRow> rows;
        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            rows = await conn.QueryAsync<SlaRuleRow>(
                OpsGroupsDbHelper.SlaRulesListSql(db),
                new { request.TenantId },
                commandTimeout: 10);
        }

        var all         = rows.ToList();
        var globalRules = all.Where(r => r.Level == "global").Select(MapRuleOut).ToList();
        var seriesRules = all.Where(r => r.Level == "tour_series").Select(MapRuleOut).ToList();

        return TypedResults.Ok(new
        {
            global          = globalRules,
            series_overrides = seriesRules,
        });
    }

    // -------------------------------------------------------------------------
    // Save
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleSaveAsync(
        SaveRequest                      request,
        HttpContext                      httpContext,
        IDbConnectionFactory             connectionFactory,
        IOptions<OpsGroupsDbSettings>    opsGroupsDbOptions,
        CancellationToken                ct)
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

        if (request.Rules is null || request.Rules.Count == 0)
            return TypedResults.Ok(new { success = true, saved_count = 0, rules = Array.Empty<object>() });

        OpsGroupsDbSettings db  = opsGroupsDbOptions.Value;
        DateTimeOffset      now = OpsGroupsDbHelper.UtcNow();
        string              sql = OpsGroupsDbHelper.SlaRuleUpsertSql(db);

        using IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType);

        foreach (SaveRuleItem rule in request.Rules)
        {
            string scopeKey = rule.Level == "global" ? "global" : $"ts_{rule.ScopeCode}";
            await conn.ExecuteAsync(
                sql,
                new
                {
                    Id             = Guid.CreateVersion7(),
                    request.TenantId,
                    Level          = rule.Level,
                    ScopeKey       = scopeKey,
                    TourCode       = (string?)null,
                    GroupTaskCode  = rule.GroupTaskCode,
                    ReferenceDate  = rule.ReferenceDate,
                    OffsetDays     = rule.OffsetDays,
                    Version        = OpsGroupsDbHelper.ComputeVersionToken(now),
                    Now            = now,
                    UpdatedBy      = request.UserId,
                },
                commandTimeout: 10);
        }

        var savedRows = await conn.QueryAsync<SlaRuleRow>(
            OpsGroupsDbHelper.SlaRulesListSql(db),
            new { request.TenantId },
            commandTimeout: 10);

        return TypedResults.Ok(new
        {
            success    = true,
            saved_count = request.Rules.Count,
            rules      = savedRows.Select(MapRuleOut).ToList(),
        });
    }

    private static object MapRuleOut(SlaRuleRow r) => new
    {
        rule_id         = r.Id,
        level           = r.Level,
        group_task_code = r.GroupTaskCode,
        offset_days     = r.GroupTaskSlaOffsetDays,
        reference_date  = r.ReferenceDate,
        required        = true,
        critical        = false,
    };

    // -------------------------------------------------------------------------
    // DTOs
    // -------------------------------------------------------------------------
    private sealed record FetchRequest : RequestContext;

    private sealed record SaveRequest : RequestContext
    {
        public List<SaveRuleItem>? Rules { get; init; }
    }

    private sealed record SaveRuleItem(
        string  Level,
        string? ScopeCode,
        string  GroupTaskCode,
        int?    OffsetDays,
        string  ReferenceDate,
        bool    Required,
        bool    Critical);
}
