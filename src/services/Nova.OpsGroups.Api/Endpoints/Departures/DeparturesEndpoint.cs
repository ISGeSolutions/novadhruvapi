using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.OpsGroups.Api.Configuration;
using Nova.OpsGroups.Api.Endpoints;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.OpsGroups.Api.Endpoints.Departures;

public static class DeparturesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/grouptour-task-departures", HandleListAsync)
             .RequireAuthorization()
             .WithName("DeparturesList");

        group.MapPost("/grouptour-task-departures/{departure_id}", HandleDetailAsync)
             .RequireAuthorization()
             .WithName("DeparturesDetail");
    }

    // -------------------------------------------------------------------------
    // List
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleListAsync(
        ListRequest                      request,
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

        bool hasOpenFilter = !string.IsNullOrEmpty(request.OpsManager)
                          || !string.IsNullOrEmpty(request.OpsExec)
                          || !string.IsNullOrEmpty(request.TourGenericCode);
        if (!hasOpenFilter && (!request.DateFrom.HasValue || !request.DateTo.HasValue))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["date_from"] = ["date_from and date_to are required unless ops_manager, ops_exec, or tour_generic_code is set."],
                },
                title: "Validation failed");

        int pageSize = Math.Min(request.PageSize ?? 100, 500);
        int page     = Math.Max(request.Page     ?? 1,   1);
        int skip     = (page - 1) * pageSize;

        OpsGroupsDbSettings db = opsGroupsDbOptions.Value;

        var filters = new DepartureFilters(
            request.DateFrom,
            request.DateTo,
            request.BranchCode,
            request.SeriesCode,
            request.TourGenericCode,
            request.DestinationCode,
            request.OpsManager,
            request.OpsExec,
            string.IsNullOrEmpty(request.Search) ? null : $"%{request.Search}%",
            BranchCodeFilter: request.BranchCodeFilter);

        var p = BuildFilterParams(request);

        IEnumerable<DepartureRow>   departures;
        IEnumerable<GroupTaskRow>   allTasks;
        BusinessRulesRow?           rules;
        int                         total;

        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            p.Add("Skip",     skip);
            p.Add("Take",     pageSize);

            departures = await conn.QueryAsync<DepartureRow>(
                OpsGroupsDbHelper.DeparturesListSql(db, filters, skip, pageSize),
                p,
                commandTimeout: 15);

            total = await conn.ExecuteScalarAsync<int>(
                OpsGroupsDbHelper.DeparturesCountSql(db, filters),
                p,
                commandTimeout: 15);

            var depIds = departures.Select(d => d.DepartureId).ToList();
            allTasks = depIds.Count == 0
                ? Enumerable.Empty<GroupTaskRow>()
                : await conn.QueryAsync<GroupTaskRow>(
                    OpsGroupsDbHelper.GroupTasksByDepartureIdsSql(db),
                    new { request.TenantId, DepartureIds = depIds },
                    commandTimeout: 15);

            rules = await conn.QueryFirstOrDefaultAsync<BusinessRulesRow>(
                OpsGroupsDbHelper.BusinessRulesFetchSql(db),
                new { request.TenantId, request.CompanyCode, BranchCode = request.BranchCode ?? string.Empty },
                commandTimeout: 10);
        }

        var tasksByDep = allTasks.GroupBy(t => t.DepartureId)
                                  .ToDictionary(g => g.Key, g => g.ToList());

        string readinessMethod      = rules?.ReadinessMethod      ?? "required_only";
        bool   includeNaInReadiness = rules?.IncludeNaInReadiness ?? false;
        bool     ignorComplete = request.IgnoreComplete ?? false;
        string   projection    = request.Projection ?? "full";
        string?  quickStatus   = request.QuickStatus;
        DateOnly today         = DateOnly.FromDateTime(DateTime.UtcNow);

        var rows = departures
            .Select(d =>
            {
                var tasks        = tasksByDep.TryGetValue(d.DepartureId, out var t) ? t : new();
                int readiness    = OpsGroupsDbHelper.ComputeReadinessPct(tasks, readinessMethod, includeNaInReadiness);
                string riskLevel = OpsGroupsDbHelper.ComputeRiskLevel(readiness);
                return (dep: d, tasks, readiness, riskLevel);
            })
            .Where(r => !ignorComplete || r.tasks.Any(t => t.Status is not "complete" and not "not_applicable"))
            .Where(r => quickStatus is null || MatchesQuickStatus(r.tasks, r.readiness, today, quickStatus))
            .Select(r => BuildDepartureShape(r.dep, r.tasks, r.readiness, r.riskLevel, projection))
            .ToList();

        return TypedResults.Ok(new
        {
            departures = rows,
            total,
            page,
            page_size = pageSize,
        });
    }

    // -------------------------------------------------------------------------
    // Detail
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleDetailAsync(
        string                           departure_id,
        DetailRequest                    request,
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

        DepartureRow?             dep;
        IEnumerable<GroupTaskRow> tasks;
        BusinessRulesRow?         rules;

        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            dep = await conn.QueryFirstOrDefaultAsync<DepartureRow>(
                OpsGroupsDbHelper.DepartureByIdSql(db),
                new { request.TenantId, DepartureId = departure_id },
                commandTimeout: 10);

            if (dep is null)
                return TypedResults.Problem(
                    title:      "Not found",
                    detail:     $"Departure '{departure_id}' not found.",
                    statusCode: StatusCodes.Status404NotFound);

            tasks = await conn.QueryAsync<GroupTaskRow>(
                OpsGroupsDbHelper.GroupTasksByDepartureIdsSql(db),
                new { request.TenantId, DepartureIds = new[] { departure_id } },
                commandTimeout: 10);

            rules = await conn.QueryFirstOrDefaultAsync<BusinessRulesRow>(
                OpsGroupsDbHelper.BusinessRulesFetchSql(db),
                new { request.TenantId, request.CompanyCode, request.BranchCode },
                commandTimeout: 10);
        }

        var taskList    = tasks.ToList();
        int readiness   = OpsGroupsDbHelper.ComputeReadinessPct(
            taskList,
            rules?.ReadinessMethod      ?? "required_only",
            rules?.IncludeNaInReadiness ?? false);
        string riskLevel = OpsGroupsDbHelper.ComputeRiskLevel(readiness);

        DateTimeOffset lastUpdated = taskList.Count == 0
            ? dep.UpdatedOn
            : taskList.Max(t => t.UpdatedOn) > dep.UpdatedOn
                ? taskList.Max(t => t.UpdatedOn)
                : dep.UpdatedOn;

        return TypedResults.Ok(new
        {
            departure_id     = dep.DepartureId,
            series_code      = dep.SeriesCode,
            series_name      = dep.SeriesName,
            departure_date   = dep.DepartureDate,
            return_date      = dep.ReturnDate,
            destination_code = dep.DestinationCode,
            destination_name = dep.DestinationName,
            branch_code      = dep.BranchCode,
            pax_count        = dep.PaxCount,
            booking_count    = dep.BookingCount,
            ops_manager      = dep.OpsManagerName,
            ops_exec         = dep.OpsExecName,
            gtd              = dep.Gtd,
            notes            = dep.Notes,
            group_tasks      = taskList.Select(MapTask).ToList(),
            readiness_pct    = readiness,
            risk_level       = riskLevel,
            last_updated     = lastUpdated,
        });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private static DynamicParameters BuildFilterParams(ListRequest request)
    {
        var p = new DynamicParameters();
        p.Add("TenantId", request.TenantId);
        if (request.DateFrom.HasValue)                           p.Add("DateFrom",         request.DateFrom);
        if (request.DateTo.HasValue)                             p.Add("DateTo",           request.DateTo);
        if (request.BranchCodeFilter is { Length: > 0 })        p.Add("BranchCodeFilter", request.BranchCodeFilter);
        else if (!string.IsNullOrEmpty(request.BranchCode))     p.Add("BranchCode",       request.BranchCode);
        if (!string.IsNullOrEmpty(request.TourGenericCode))     p.Add("TourGenericCode",  request.TourGenericCode);
        if (!string.IsNullOrEmpty(request.SeriesCode))          p.Add("SeriesCode",       request.SeriesCode);
        if (!string.IsNullOrEmpty(request.DestinationCode))     p.Add("DestinationCode",  request.DestinationCode);
        if (!string.IsNullOrEmpty(request.OpsManager))          p.Add("OpsManager",       request.OpsManager);
        if (!string.IsNullOrEmpty(request.OpsExec))             p.Add("OpsExec",          request.OpsExec);
        if (!string.IsNullOrEmpty(request.Search))              p.Add("Search",           $"%{request.Search}%");
        return p;
    }

    private static object BuildDepartureShape(DepartureRow dep, List<GroupTaskRow> tasks, int readiness, string riskLevel, string projection)
    {
        if (projection == "calendar")
        {
            return new
            {
                departure_id     = dep.DepartureId,
                departure_date   = dep.DepartureDate,
                destination_name = dep.DestinationName,
                readiness_pct    = readiness,
                risk_level       = riskLevel,
            };
        }

        var baseShape = new
        {
            departure_id     = dep.DepartureId,
            series_code      = dep.SeriesCode,
            series_name      = dep.SeriesName,
            departure_date   = dep.DepartureDate,
            return_date      = dep.ReturnDate,
            destination_code = dep.DestinationCode,
            destination_name = dep.DestinationName,
            branch_code      = dep.BranchCode,
            pax_count        = dep.PaxCount,
            booking_count    = dep.BookingCount,
            ops_manager      = dep.OpsManagerName,
            ops_exec         = dep.OpsExecName,
            gtd              = dep.Gtd,
            notes            = dep.Notes,
            readiness_pct    = readiness,
            risk_level       = riskLevel,
        };

        if (projection == "tasks" || projection == "full")
        {
            return new
            {
                baseShape.departure_id,
                baseShape.series_code,
                baseShape.series_name,
                baseShape.departure_date,
                baseShape.return_date,
                baseShape.destination_code,
                baseShape.destination_name,
                baseShape.branch_code,
                baseShape.pax_count,
                baseShape.booking_count,
                baseShape.ops_manager,
                baseShape.ops_exec,
                baseShape.gtd,
                baseShape.notes,
                group_tasks   = tasks.Select(MapTask).ToList(),
                baseShape.readiness_pct,
                baseShape.risk_level,
            };
        }

        return baseShape;
    }

    private static bool MatchesQuickStatus(List<GroupTaskRow> tasks, int readiness, DateOnly today, string quickStatus) =>
        quickStatus switch
        {
            "overdue"    => tasks.Any(t => t.Status == "overdue"),
            "at_risk"    => OpsGroupsDbHelper.ComputeRiskLevel(readiness) is "red" or "amber",
            "ready"      => readiness >= 100,
            "due_later"  => tasks.Any(t => t.DueDate.HasValue && t.DueDate.Value == today),
            "done_today" => tasks.Any(t => t.Status == "complete" && t.CompletedDate.HasValue && t.CompletedDate.Value == today),
            "done_past"  => tasks.Any(t => t.Status == "complete" && t.CompletedDate.HasValue && t.CompletedDate.Value < today),
            _            => true,
        };

    private static object MapTask(GroupTaskRow t) => new
    {
        group_task_id   = t.GroupTaskId,
        template_code   = t.TemplateCode,
        status          = t.Status,
        due_date        = t.DueDate,
        completed_date  = t.CompletedDate,
        notes           = t.Notes,
        source          = t.Source,
    };

    // -------------------------------------------------------------------------
    // DTOs
    // -------------------------------------------------------------------------
    private sealed record ListRequest : RequestContext
    {
        public DateOnly?   DateFrom         { get; init; }
        public DateOnly?   DateTo           { get; init; }
        public new string? BranchCode       { get; init; }
        public string?     TourGenericCode  { get; init; }
        public string?     SeriesCode       { get; init; }
        public string?     DestinationCode  { get; init; }
        public string?     OpsManager       { get; init; }
        public string?     OpsExec          { get; init; }
        public string?     Search           { get; init; }
        public bool?       IgnoreComplete   { get; init; }
        public string?     QuickStatus      { get; init; }
        public string?     Projection       { get; init; }
        public int?        Page             { get; init; }
        public int?        PageSize         { get; init; }
        public string[]?   BranchCodeFilter { get; init; }
    }

    private sealed record DetailRequest : RequestContext;
}
