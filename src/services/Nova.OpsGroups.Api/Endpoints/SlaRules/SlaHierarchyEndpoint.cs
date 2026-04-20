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

public static class SlaHierarchyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/group-task-sla-hierarchy",        HandleHierarchyAsync)
             .RequireAuthorization()
             .WithName("SlaHierarchy");

        group.MapPatch("/group-task-sla-rule-save",       HandleRuleSaveAsync)
             .RequireAuthorization()
             .WithName("SlaRuleSave");

        group.MapPost("/group-task-sla-audit",            HandleAuditAsync)
             .RequireAuthorization()
             .WithName("SlaAudit");

        group.MapPost("/group-task-codes-available",      HandleCodesAvailableAsync)
             .RequireAuthorization()
             .WithName("SlaCodesAvailable");
    }

    // -------------------------------------------------------------------------
    // Hierarchy
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleHierarchyAsync(
        HierarchyRequest                 request,
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

        IEnumerable<SlaRuleRow> allRows;
        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            allRows = await conn.QueryAsync<SlaRuleRow>(
                OpsGroupsDbHelper.SlaRulesForTenantSql(db),
                new { request.TenantId },
                commandTimeout: 10);
        }

        var rows = allRows.ToList();

        // Build global level
        var globalRules = rows.Where(r => r.Level == "global").ToList();
        var allCodes    = rows.Select(r => r.GroupTaskCode).Distinct().OrderBy(c => c).ToList();
        var allRefs     = rows.Select(r => r.ReferenceDate).Distinct().OrderBy(r => r).ToList();

        var globalEntries = allRefs.Select(refDate =>
        {
            var offsets = allCodes.ToDictionary(
                code => code,
                code => (object?)globalRules.FirstOrDefault(r => r.GroupTaskCode == code && r.ReferenceDate == refDate)?.GroupTaskSlaOffsetDays);
            return new { reference_date = refDate, offsets };
        }).ToList();

        var globalUpdated = globalRules.Any() ? globalRules.Max(r => r.UpdatedOn) : (DateTimeOffset?)null;
        var global = new
        {
            scope_key = "global",
            version   = OpsGroupsDbHelper.ComputeVersionToken(globalUpdated),
            entries   = globalEntries,
        };

        // Build series levels
        var seriesRules  = rows.Where(r => r.Level == "tour_series").ToList();
        var seriesKeys   = seriesRules.Select(r => r.ScopeKey).Distinct().OrderBy(k => k).ToList();

        var levels = seriesKeys.Select(scopeKey =>
        {
            var scopeRules   = seriesRules.Where(r => r.ScopeKey == scopeKey).ToList();
            var scopeUpdated = scopeRules.Any() ? scopeRules.Max(r => r.UpdatedOn) : (DateTimeOffset?)null;
            var entries = allRefs.Select(refDate =>
            {
                var offsets = allCodes.ToDictionary(
                    code => code,
                    code => (object?)scopeRules.FirstOrDefault(r => r.GroupTaskCode == code && r.ReferenceDate == refDate)?.GroupTaskSlaOffsetDays);
                return new { reference_date = refDate, offsets };
            }).ToList();
            return new
            {
                level_id  = scopeKey,
                level_type = "tour_series",
                version   = OpsGroupsDbHelper.ComputeVersionToken(scopeUpdated),
                entries,
            };
        }).ToList();

        return TypedResults.Ok(new { global, levels });
    }

    // -------------------------------------------------------------------------
    // Per-grid rule save
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleRuleSaveAsync(
        RuleSaveRequest                  request,
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
            return TypedResults.Ok(new { level_id = request.LevelId, saved_at = OpsGroupsDbHelper.UtcNow(), saved_count = 0 });

        OpsGroupsDbSettings db  = opsGroupsDbOptions.Value;
        DateTimeOffset      now = OpsGroupsDbHelper.UtcNow();

        using IDbConnection  conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType);
        conn.Open();
        using IDbTransaction tx   = conn.BeginTransaction();

        // Optimistic check — verify old_value matches current for each change
        foreach (RuleSaveChange change in request.Changes)
        {
            var current = await conn.QueryFirstOrDefaultAsync<SlaRuleRow>(
                $"""
                SELECT group_task_sla_offset_days AS GroupTaskSlaOffsetDays, id AS Id, level AS Level,
                       scope_key AS ScopeKey, tour_code AS TourCode, group_task_code AS GroupTaskCode,
                       reference_date AS ReferenceDate, version AS Version, updated_on AS UpdatedOn
                FROM   {OpsGroupsDbHelper.Dialect(db.DbType).TableRef("opsgroups", "grouptour_sla_rules")}
                WHERE  tenant_id       = @TenantId
                AND    scope_key       = @ScopeKey
                AND    group_task_code = @GroupTaskCode
                AND    reference_date  = @ReferenceDate
                """,
                new { request.TenantId, ScopeKey = request.LevelId, change.GroupTaskCode, change.ReferenceDate },
                tx,
                commandTimeout: 10);

            int? currentValue = current?.GroupTaskSlaOffsetDays;
            if (currentValue != change.OldValue)
            {
                tx.Rollback();
                return TypedResults.Problem(
                    title:      "Conflict",
                    detail:     $"Current value for {change.GroupTaskCode}/{change.ReferenceDate} does not match old_value.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }

        // Apply changes
        string scopeLabel = $"{request.LevelType}:{request.LevelId}";
        int    savedCount = 0;

        foreach (RuleSaveChange change in request.Changes)
        {
            // Null new_value at non-global level = delete override
            if (change.NewValue is null && request.LevelType != "global")
            {
                await conn.ExecuteAsync(
                    OpsGroupsDbHelper.SlaRuleDeleteSql(db),
                    new { request.TenantId, ScopeKey = request.LevelId, change.GroupTaskCode, change.ReferenceDate },
                    tx,
                    commandTimeout: 10);
            }
            else
            {
                await conn.ExecuteAsync(
                    OpsGroupsDbHelper.SlaRuleUpsertSql(db),
                    new
                    {
                        Id            = Guid.CreateVersion7(),
                        request.TenantId,
                        Level         = request.LevelType,
                        ScopeKey      = request.LevelId,
                        TourCode      = request.TourCode,
                        GroupTaskCode = change.GroupTaskCode,
                        ReferenceDate = change.ReferenceDate,
                        OffsetDays    = change.NewValue,
                        Version       = OpsGroupsDbHelper.ComputeVersionToken(now),
                        Now           = now,
                        UpdatedBy     = request.UserId,
                    },
                    tx,
                    commandTimeout: 10);
            }

            // Write audit row
            await conn.ExecuteAsync(
                OpsGroupsDbHelper.SlaAuditInsertSql(db),
                new
                {
                    Id              = Guid.CreateVersion7(),
                    request.TenantId,
                    ScopeKey        = request.LevelId,
                    ScopeLabel      = scopeLabel,
                    GroupTaskCode   = change.GroupTaskCode,
                    ReferenceDate   = change.ReferenceDate,
                    OldValue        = change.OldValue,
                    NewValue        = change.NewValue,
                    ChangedByName   = request.UserId,
                    ChangedAt       = now,
                },
                tx,
                commandTimeout: 10);

            savedCount++;
        }

        tx.Commit();
        return TypedResults.Ok(new { level_id = request.LevelId, saved_at = now, saved_count = savedCount });
    }

    // -------------------------------------------------------------------------
    // Audit
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleAuditAsync(
        AuditRequest                     request,
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

        int pageSize = Math.Min(request.PageSize ?? 50, 200);
        int page     = Math.Max(request.Page     ?? 1,  1);
        int skip     = (page - 1) * pageSize;

        OpsGroupsDbSettings db = opsGroupsDbOptions.Value;

        IEnumerable<AuditRow> rows;
        int total;
        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            total = await conn.ExecuteScalarAsync<int>(
                OpsGroupsDbHelper.SlaAuditCountSql(db),
                new { request.TenantId, ScopeKey = request.ScopeKey },
                commandTimeout: 10);

            rows = await conn.QueryAsync<AuditRow>(
                OpsGroupsDbHelper.SlaAuditQuerySql(db),
                new { request.TenantId, ScopeKey = request.ScopeKey },
                commandTimeout: 10);
        }

        var paged = rows.Skip(skip).Take(pageSize).ToList();
        string scopeLabel = paged.Any() ? paged.First().ScopeLabel : (request.ScopeKey ?? string.Empty);

        return TypedResults.Ok(new
        {
            scope_key   = request.ScopeKey,
            scope_label = scopeLabel,
            page,
            page_size   = pageSize,
            total,
            entries     = paged.Select(r => new
            {
                id              = r.Id,
                group_task_code = r.GroupTaskCode,
                reference_date  = r.ReferenceDate,
                old_value       = r.OldValue,
                new_value       = r.NewValue,
                changed_by      = r.ChangedByName,
                changed_at      = r.ChangedAt,
            }).ToList(),
        });
    }

    // -------------------------------------------------------------------------
    // Codes available
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleCodesAvailableAsync(
        CodesAvailableRequest            request,
        HttpContext                      httpContext,
        IDbConnectionFactory             connectionFactory,
        IOptions<OpsGroupsDbSettings>    opsGroupsDbOptions,
        IOptions<PresetsDbSettings>      presetsDbOptions,
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

        PresetsDbSettings presetsDb = presetsDbOptions.Value;

        IEnumerable<TaskTemplateRow> templates;
        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(presetsDb.ConnectionString, presetsDb.DbType))
        {
            templates = await conn.QueryAsync<TaskTemplateRow>(
                OpsGroupsDbHelper.TaskTemplatesForTenantSql(presetsDb.DbType),
                new { request.TenantId },
                commandTimeout: 10);
        }

        return TypedResults.Ok(new
        {
            available_codes = templates.Select(t => new
            {
                code   = t.Code,
                name   = t.Name,
                source = t.Source,
            }).ToList(),
        });
    }

    // -------------------------------------------------------------------------
    // DTOs
    // -------------------------------------------------------------------------
    private sealed record HierarchyRequest : RequestContext
    {
        public string? TourGenericCode { get; init; }
        public int?    YearFloor       { get; init; }
    }

    private sealed record RuleSaveRequest : RequestContext
    {
        public string?            LevelId   { get; init; }
        public string?            LevelType { get; init; }
        public string?            TourCode  { get; init; }
        public List<RuleSaveChange>? Changes { get; init; }
    }

    private sealed record RuleSaveChange(
        string GroupTaskCode,
        string ReferenceDate,
        int?   OldValue,
        int?   NewValue);

    private sealed record AuditRequest : RequestContext
    {
        public string? ScopeKey { get; init; }
        public int?    Page     { get; init; }
        public int?    PageSize { get; init; }
    }

    private sealed record CodesAvailableRequest : RequestContext
    {
        public string? TourGenericCode { get; init; }
    }

    private sealed record AuditRow(
        Guid           Id,
        string         ScopeKey,
        string         ScopeLabel,
        string         GroupTaskCode,
        string         ReferenceDate,
        int?           OldValue,
        int?           NewValue,
        string         ChangedByName,
        DateTimeOffset ChangedAt);

    private sealed record TaskTemplateRow(string Code, string Name, string Source);
}
