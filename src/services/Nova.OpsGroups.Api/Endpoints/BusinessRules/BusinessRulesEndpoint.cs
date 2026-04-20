using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.OpsGroups.Api.Configuration;
using Nova.OpsGroups.Api.Endpoints;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.OpsGroups.Api.Endpoints.BusinessRules;

public static class BusinessRulesEndpoint
{
    private static readonly HashSet<string> ValidReadinessMethods  = new(StringComparer.OrdinalIgnoreCase)
        { "required_only", "all_tasks" };

    private static readonly HashSet<string> ValidRiskThresholds = new(StringComparer.OrdinalIgnoreCase)
        { "critical_overdue", "any_overdue", "no_overdue" };

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/group-task-business-rules",  HandleFetchAsync)
             .RequireAuthorization()
             .WithName("BusinessRulesFetch");

        group.MapPatch("/group-task-business-rules", HandleSaveAsync)
             .RequireAuthorization()
             .WithName("BusinessRulesSave");
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

        BusinessRulesRow? row;
        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            row = await conn.QueryFirstOrDefaultAsync<BusinessRulesRow>(
                OpsGroupsDbHelper.BusinessRulesFetchSql(db),
                new { request.TenantId, request.CompanyCode, request.BranchCode },
                commandTimeout: 10);
        }

        return TypedResults.Ok(MapToResponse(row, request.TenantId, request.CompanyCode, request.BranchCode));
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

        if (request.Changes is null || request.Changes.Count == 0)
        {
            OpsGroupsDbSettings dbR = opsGroupsDbOptions.Value;
            BusinessRulesRow?   cur;
            using (IDbConnection conn = connectionFactory.CreateFromConnectionString(dbR.ConnectionString, dbR.DbType))
            {
                cur = await conn.QueryFirstOrDefaultAsync<BusinessRulesRow>(
                    OpsGroupsDbHelper.BusinessRulesFetchSql(dbR),
                    new { request.TenantId, request.CompanyCode, request.BranchCode },
                    commandTimeout: 10);
            }
            return TypedResults.Ok(MapToResponse(cur, request.TenantId, request.CompanyCode, request.BranchCode));
        }

        // Validate field names
        var unknownFields = request.Changes.Keys
            .Where(k => !KnownFields.Contains(k))
            .ToList();
        if (unknownFields.Count > 0)
        {
            return TypedResults.Problem(
                title:      "Unprocessable Content",
                detail:     "Unknown fields in changes.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?> { ["unknown_fields"] = unknownFields });
        }

        OpsGroupsDbSettings db = opsGroupsDbOptions.Value;

        BusinessRulesRow? existing;
        using (IDbConnection connR = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            existing = await connR.QueryFirstOrDefaultAsync<BusinessRulesRow>(
                OpsGroupsDbHelper.BusinessRulesFetchSql(db),
                new { request.TenantId, request.CompanyCode, request.BranchCode },
                commandTimeout: 10);
        }

        // Start from defaults + existing
        var current = existing is not null
            ? existing
            : new BusinessRulesRow(request.TenantId, request.CompanyCode, request.BranchCode,
                3, 7, "required_only", "critical_overdue", "any_overdue", "no_overdue",
                39, 79, true, false, OpsGroupsDbHelper.UtcNow(), request.UserId);

        // Apply changes with optimistic check
        var conflictErrors = new List<string>();
        int? newCritical   = current.OverdueCriticalDays;
        int? newWarning    = current.OverdueWarningDays;
        string newReadiness = current.ReadinessMethod;
        string newRedThresh = current.RiskRedThreshold;
        string newAmberThresh = current.RiskAmberThreshold;
        string newGreenThresh = current.RiskGreenThreshold;
        int?   newHeatRed   = current.HeatmapRedMax;
        int?   newHeatAmber = current.HeatmapAmberMax;
        bool   newAutoOverdue = current.AutoMarkOverdue;
        bool   newIncludeNa   = current.IncludeNaInReadiness;

        foreach ((string field, FieldChange change) in request.Changes)
        {
            switch (field)
            {
                case "overdue_critical_days":
                    if (!MatchesInt(change.Old, current.OverdueCriticalDays)) { conflictErrors.Add(field); break; }
                    newCritical = change.New is null ? current.OverdueCriticalDays : Convert.ToInt32(change.New);
                    break;
                case "overdue_warning_days":
                    if (!MatchesInt(change.Old, current.OverdueWarningDays)) { conflictErrors.Add(field); break; }
                    newWarning = change.New is null ? current.OverdueWarningDays : Convert.ToInt32(change.New);
                    break;
                case "readiness_method":
                    if (!MatchesStr(change.Old, current.ReadinessMethod)) { conflictErrors.Add(field); break; }
                    newReadiness = change.New?.ToString() ?? current.ReadinessMethod;
                    break;
                case "risk_red_threshold":
                    if (!MatchesStr(change.Old, current.RiskRedThreshold)) { conflictErrors.Add(field); break; }
                    newRedThresh = change.New?.ToString() ?? current.RiskRedThreshold;
                    break;
                case "risk_amber_threshold":
                    if (!MatchesStr(change.Old, current.RiskAmberThreshold)) { conflictErrors.Add(field); break; }
                    newAmberThresh = change.New?.ToString() ?? current.RiskAmberThreshold;
                    break;
                case "risk_green_threshold":
                    if (!MatchesStr(change.Old, current.RiskGreenThreshold)) { conflictErrors.Add(field); break; }
                    newGreenThresh = change.New?.ToString() ?? current.RiskGreenThreshold;
                    break;
                case "heatmap_red_max":
                    if (!MatchesInt(change.Old, current.HeatmapRedMax)) { conflictErrors.Add(field); break; }
                    newHeatRed = change.New is null ? current.HeatmapRedMax : Convert.ToInt32(change.New);
                    break;
                case "heatmap_amber_max":
                    if (!MatchesInt(change.Old, current.HeatmapAmberMax)) { conflictErrors.Add(field); break; }
                    newHeatAmber = change.New is null ? current.HeatmapAmberMax : Convert.ToInt32(change.New);
                    break;
                case "auto_mark_overdue":
                    if (!MatchesBool(change.Old, current.AutoMarkOverdue)) { conflictErrors.Add(field); break; }
                    newAutoOverdue = change.New is null ? current.AutoMarkOverdue : Convert.ToBoolean(change.New);
                    break;
                case "include_na_in_readiness":
                    if (!MatchesBool(change.Old, current.IncludeNaInReadiness)) { conflictErrors.Add(field); break; }
                    newIncludeNa = change.New is null ? current.IncludeNaInReadiness : Convert.ToBoolean(change.New);
                    break;
            }
        }

        if (conflictErrors.Count > 0)
            return TypedResults.Problem(
                title:      "Conflict",
                detail:     "One or more field values did not match the current state.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["conflicting_fields"] = conflictErrors });

        // Validate final state
        if (newCritical < 0 || newCritical > newWarning)
            return TypedResults.Problem(
                title:      "Unprocessable Content",
                detail:     "overdue_critical_days must be >= 0 and <= overdue_warning_days.",
                statusCode: StatusCodes.Status422UnprocessableEntity);

        if (newHeatRed >= newHeatAmber || newHeatAmber > 100)
            return TypedResults.Problem(
                title:      "Unprocessable Content",
                detail:     "heatmap_red_max must be < heatmap_amber_max, and heatmap_amber_max <= 100.",
                statusCode: StatusCodes.Status422UnprocessableEntity);

        if (!ValidReadinessMethods.Contains(newReadiness))
            return TypedResults.Problem(
                title:      "Unprocessable Content",
                detail:     $"readiness_method must be one of: {string.Join(", ", ValidReadinessMethods)}.",
                statusCode: StatusCodes.Status422UnprocessableEntity);

        DateTimeOffset now = OpsGroupsDbHelper.UtcNow();

        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            await conn.ExecuteAsync(
                OpsGroupsDbHelper.BusinessRulesUpsertSql(db),
                new
                {
                    request.TenantId,
                    request.CompanyCode,
                    request.BranchCode,
                    OverdueCriticalDays   = newCritical,
                    OverdueWarningDays    = newWarning,
                    ReadinessMethod       = newReadiness,
                    RiskRedThreshold      = newRedThresh,
                    RiskAmberThreshold    = newAmberThresh,
                    RiskGreenThreshold    = newGreenThresh,
                    HeatmapRedMax         = newHeatRed,
                    HeatmapAmberMax       = newHeatAmber,
                    AutoMarkOverdue       = newAutoOverdue,
                    IncludeNaInReadiness  = newIncludeNa,
                    Now                   = now,
                    UpdatedBy             = request.UserId,
                },
                commandTimeout: 10);
        }

        var saved = new BusinessRulesRow(
            request.TenantId, request.CompanyCode, request.BranchCode,
            newCritical!.Value, newWarning!.Value, newReadiness,
            newRedThresh, newAmberThresh, newGreenThresh,
            newHeatRed!.Value, newHeatAmber!.Value, newAutoOverdue, newIncludeNa,
            now, request.UserId);

        return TypedResults.Ok(MapToResponse(saved, request.TenantId, request.CompanyCode, request.BranchCode));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private static object MapToResponse(BusinessRulesRow? row, string tenantId, string companyCode, string branchCode) =>
        row is not null
        ? new
        {
            tenant_id               = row.TenantId,
            company_code            = row.CompanyCode,
            branch_code             = row.BranchCode,
            overdue_critical_days   = row.OverdueCriticalDays,
            overdue_warning_days    = row.OverdueWarningDays,
            readiness_method        = row.ReadinessMethod,
            risk_red_threshold      = row.RiskRedThreshold,
            risk_amber_threshold    = row.RiskAmberThreshold,
            risk_green_threshold    = row.RiskGreenThreshold,
            heatmap_red_max         = row.HeatmapRedMax,
            heatmap_amber_max       = row.HeatmapAmberMax,
            auto_mark_overdue       = row.AutoMarkOverdue,
            include_na_in_readiness = row.IncludeNaInReadiness,
            updated_at              = row.UpdatedAt,
            updated_by              = row.UpdatedBy,
        }
        : new
        {
            tenant_id               = tenantId,
            company_code            = companyCode,
            branch_code             = branchCode,
            overdue_critical_days   = 3,
            overdue_warning_days    = 7,
            readiness_method        = "required_only",
            risk_red_threshold      = "critical_overdue",
            risk_amber_threshold    = "any_overdue",
            risk_green_threshold    = "no_overdue",
            heatmap_red_max         = 39,
            heatmap_amber_max       = 79,
            auto_mark_overdue       = true,
            include_na_in_readiness = false,
            updated_at              = (DateTimeOffset?)null,
            updated_by              = (string?)null,
        };

    private static bool MatchesInt(object? oldVal, int currentVal)
    {
        if (oldVal is null)        return currentVal == 0;
        if (oldVal is int i)       return i == currentVal;
        if (oldVal is long l)      return l == currentVal;
        if (int.TryParse(oldVal.ToString(), out int parsed)) return parsed == currentVal;
        return false;
    }

    private static bool MatchesStr(object? oldVal, string currentVal) =>
        string.Equals(oldVal?.ToString(), currentVal, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesBool(object? oldVal, bool currentVal)
    {
        if (oldVal is bool b) return b == currentVal;
        if (bool.TryParse(oldVal?.ToString(), out bool parsed)) return parsed == currentVal;
        return false;
    }

    private static readonly HashSet<string> KnownFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "overdue_critical_days", "overdue_warning_days", "readiness_method",
        "risk_red_threshold", "risk_amber_threshold", "risk_green_threshold",
        "heatmap_red_max", "heatmap_amber_max", "auto_mark_overdue", "include_na_in_readiness",
    };

    // -------------------------------------------------------------------------
    // DTOs
    // -------------------------------------------------------------------------
    private sealed record FetchRequest : RequestContext;

    private sealed record SaveRequest : RequestContext
    {
        public Dictionary<string, FieldChange>? Changes { get; init; }
    }

    private sealed record FieldChange(object? Old, object? New);
}
