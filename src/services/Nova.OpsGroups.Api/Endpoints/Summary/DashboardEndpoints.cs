using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.OpsGroups.Api.Configuration;
using Nova.OpsGroups.Api.Endpoints;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.OpsGroups.Api.Endpoints.Summary;

public static class DashboardEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/grouptour-task-departures-summary",    HandleSummaryAsync)
             .RequireAuthorization()
             .WithName("DashboardSummary");

        group.MapPost("/grouptour-task-departures-facets",     HandleFacetsAsync)
             .RequireAuthorization()
             .WithName("DashboardFacets");

        group.MapPost("/grouptour-task-departures-tasks",      HandleTasksViewAsync)
             .RequireAuthorization()
             .WithName("DashboardTasks");

        group.MapPost("/grouptour-task-series-aggregate",      HandleSeriesAggregateAsync)
             .RequireAuthorization()
             .WithName("DashboardSeriesAggregate");

        group.MapPost("/grouptour-task-heatmap",               HandleHeatmapAsync)
             .RequireAuthorization()
             .WithName("DashboardHeatmap");
    }

    // -------------------------------------------------------------------------
    // Shared filter request
    // -------------------------------------------------------------------------
    private sealed record FilterRequest : RequestContext
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
        public int?        Page             { get; init; }
        public int?        PageSize         { get; init; }
        public DateOnly?   WindowStart      { get; init; }
        public string[]?   BranchCodeFilter { get; init; }
        public string?     QuickStatus      { get; init; }
    }

    private static DepartureFilters ToFilters(FilterRequest r) => new(
        r.DateFrom,
        r.DateTo,
        r.BranchCode,
        r.SeriesCode,
        r.TourGenericCode,
        r.DestinationCode,
        r.OpsManager,
        r.OpsExec,
        string.IsNullOrEmpty(r.Search) ? null : $"%{r.Search}%",
        r.WindowStart,
        r.BranchCodeFilter);

    private static DynamicParameters BuildFilterParams(FilterRequest request)
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
        if (request.WindowStart.HasValue)                       p.Add("WindowStart",      request.WindowStart);
        return p;
    }

    private static string? ValidateTenant(FilterRequest request, HttpContext httpContext)
    {
        Dictionary<string, string[]> errors = RequestContextValidator.Validate(request);
        if (errors.Count > 0) return "validation";
        string? jwtTenantId = httpContext.User.FindFirstValue("tenant_id");
        return string.Equals(request.TenantId, jwtTenantId, StringComparison.OrdinalIgnoreCase) ? null : "forbidden";
    }

    // -------------------------------------------------------------------------
    // Summary KPI
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleSummaryAsync(
        FilterRequest                    request,
        HttpContext                      httpContext,
        IDbConnectionFactory             connectionFactory,
        IOptions<OpsGroupsDbSettings>    opsGroupsDbOptions,
        CancellationToken                ct)
    {
        var check = ValidateTenant(request, httpContext);
        if (check == "validation") return TypedResults.ValidationProblem(RequestContextValidator.Validate(request), title: "Validation failed");
        if (check == "forbidden")  return TypedResults.Problem(title: "Forbidden", detail: "tenant_id does not match the authenticated tenant.", statusCode: StatusCodes.Status403Forbidden);

        OpsGroupsDbSettings db      = opsGroupsDbOptions.Value;
        DepartureFilters    filters = ToFilters(request);

        var p = BuildFilterParams(request);

        IEnumerable<DepartureRow>   departures;
        IEnumerable<GroupTaskRow>   allTasks;
        BusinessRulesRow?           rules;

        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            departures = await conn.QueryAsync<DepartureRow>(
                OpsGroupsDbHelper.DeparturesListSql(db, filters, 0, 10000),
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
        int total       = 0, atRisk = 0, ready = 0, overdue = 0, dueLater = 0, doneToday = 0, donePast = 0;
        double readSum  = 0;
        DateOnly today  = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var dep in departures)
        {
            total++;
            var tasks     = tasksByDep.TryGetValue(dep.DepartureId, out var t) ? t : new();
            int readiness = OpsGroupsDbHelper.ComputeReadinessPct(tasks, readinessMethod, includeNaInReadiness);
            readSum      += readiness;
            string risk   = OpsGroupsDbHelper.ComputeRiskLevel(readiness);

            if (risk == "red" || risk == "amber") atRisk++;
            if (readiness >= 100)                ready++;
            if (tasks.Any(t2 => t2.Status == "overdue")) overdue++;
            if (tasks.Any(t2 => t2.DueDate.HasValue && t2.DueDate.Value == today)) dueLater++;
            if (tasks.Any(t2 => t2.Status == "complete" && t2.CompletedDate.HasValue && t2.CompletedDate.Value == today)) doneToday++;
            if (tasks.Any(t2 => t2.Status == "complete" && t2.CompletedDate.HasValue && t2.CompletedDate.Value < today))  donePast++;
        }

        return TypedResults.Ok(new
        {
            total,
            at_risk      = atRisk,
            ready,
            overdue,
            due_later    = dueLater,
            done_today   = doneToday,
            done_past    = donePast,
            avg_readiness = total == 0 ? 0 : (int)Math.Round(readSum / total),
        });
    }

    // -------------------------------------------------------------------------
    // Facets
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleFacetsAsync(
        FilterRequest                    request,
        HttpContext                      httpContext,
        IDbConnectionFactory             connectionFactory,
        IOptions<OpsGroupsDbSettings>    opsGroupsDbOptions,
        CancellationToken                ct)
    {
        var check = ValidateTenant(request, httpContext);
        if (check == "validation") return TypedResults.ValidationProblem(RequestContextValidator.Validate(request), title: "Validation failed");
        if (check == "forbidden")  return TypedResults.Problem(title: "Forbidden", detail: "tenant_id does not match the authenticated tenant.", statusCode: StatusCodes.Status403Forbidden);

        OpsGroupsDbSettings db      = opsGroupsDbOptions.Value;
        DepartureFilters    filters = ToFilters(request);
        var                 p       = BuildFilterParams(request);

        IEnumerable<FacetRow>             rows;
        IEnumerable<FacetsTourGenericRow> tourGenericRows;
        int                               total;
        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            rows = await conn.QueryAsync<FacetRow>(
                OpsGroupsDbHelper.FacetsSql(db, filters),
                p,
                commandTimeout: 15);

            tourGenericRows = await conn.QueryAsync<FacetsTourGenericRow>(
                OpsGroupsDbHelper.FacetsTourGenericsSql(db, filters),
                p,
                commandTimeout: 15);

            total = await conn.ExecuteScalarAsync<int>(
                OpsGroupsDbHelper.DeparturesCountSql(db, filters),
                p,
                commandTimeout: 10);
        }

        var facets = rows.ToList();

        return TypedResults.Ok(new
        {
            branches = facets.Select(r => r.BranchCode).Distinct().OrderBy(c => c).Select(c => new { code = c, name = c }).ToList(),
            managers = facets.Where(r => !string.IsNullOrEmpty(r.OpsManagerInitials))
                             .Select(r => new { initials = r.OpsManagerInitials, name = r.OpsManagerName })
                             .DistinctBy(m => m.initials).OrderBy(m => m.name).ToList(),
            execs    = facets.Where(r => !string.IsNullOrEmpty(r.OpsExecInitials))
                             .Select(r => new { initials = r.OpsExecInitials, name = r.OpsExecName })
                             .DistinctBy(e => e.initials).OrderBy(e => e.name).ToList(),
            tour_generics = tourGenericRows.Select(r => new { code = r.TourGenericCode, name = r.TourGenericName }).ToList(),
            tour_series   = facets.Select(r => new { code = r.SeriesCode, name = r.SeriesName })
                                  .DistinctBy(s => s.code).OrderBy(s => s.code).ToList(),
            total_matching = total,
        });
    }

    // -------------------------------------------------------------------------
    // Tasks view
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleTasksViewAsync(
        FilterRequest                    request,
        HttpContext                      httpContext,
        IDbConnectionFactory             connectionFactory,
        IOptions<OpsGroupsDbSettings>    opsGroupsDbOptions,
        CancellationToken                ct)
    {
        var check = ValidateTenant(request, httpContext);
        if (check == "validation") return TypedResults.ValidationProblem(RequestContextValidator.Validate(request), title: "Validation failed");
        if (check == "forbidden")  return TypedResults.Problem(title: "Forbidden", detail: "tenant_id does not match the authenticated tenant.", statusCode: StatusCodes.Status403Forbidden);

        int pageSize = Math.Min(request.PageSize ?? 30, 200);
        int page     = Math.Max(request.Page     ?? 1,  1);
        int skip     = (page - 1) * pageSize;

        OpsGroupsDbSettings db      = opsGroupsDbOptions.Value;
        DepartureFilters    filters = ToFilters(request);
        var                 p       = BuildFilterParams(request);

        IEnumerable<DepartureRow> departures;
        IEnumerable<GroupTaskRow> allTasks;
        BusinessRulesRow?         rules;
        int                       total;

        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            departures = await conn.QueryAsync<DepartureRow>(
                OpsGroupsDbHelper.DeparturesListSql(db, filters, skip, pageSize),
                p,
                commandTimeout: 15);

            total = await conn.ExecuteScalarAsync<int>(
                OpsGroupsDbHelper.DeparturesCountSql(db, filters),
                p,
                commandTimeout: 10);

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

        var items = departures.Select(dep =>
        {
            var tasks      = tasksByDep.TryGetValue(dep.DepartureId, out var t) ? t : new();
            int readiness  = OpsGroupsDbHelper.ComputeReadinessPct(tasks, readinessMethod, includeNaInReadiness);
            string risk    = OpsGroupsDbHelper.ComputeRiskLevel(readiness);
            return new
            {
                departure = new
                {
                    departure_id     = dep.DepartureId,
                    series_code      = dep.SeriesCode,
                    series_name      = dep.SeriesName,
                    departure_date   = dep.DepartureDate,
                    destination_name = dep.DestinationName,
                    branch_code      = dep.BranchCode,
                    pax_count        = dep.PaxCount,
                    readiness_pct    = readiness,
                    risk_level       = risk,
                },
                tasks = tasks.Select(t2 => new
                {
                    group_task_id  = t2.GroupTaskId,
                    template_code  = t2.TemplateCode,
                    status         = t2.Status,
                    due_date       = t2.DueDate,
                    completed_date = t2.CompletedDate,
                }).ToList(),
            };
        }).ToList();

        return TypedResults.Ok(new { items, total, page, page_size = pageSize });
    }

    // -------------------------------------------------------------------------
    // Series aggregate
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleSeriesAggregateAsync(
        FilterRequest                    request,
        HttpContext                      httpContext,
        IDbConnectionFactory             connectionFactory,
        IOptions<OpsGroupsDbSettings>    opsGroupsDbOptions,
        CancellationToken                ct)
    {
        var check = ValidateTenant(request, httpContext);
        if (check == "validation") return TypedResults.ValidationProblem(RequestContextValidator.Validate(request), title: "Validation failed");
        if (check == "forbidden")  return TypedResults.Problem(title: "Forbidden", detail: "tenant_id does not match the authenticated tenant.", statusCode: StatusCodes.Status403Forbidden);

        OpsGroupsDbSettings db      = opsGroupsDbOptions.Value;
        DepartureFilters    filters = ToFilters(request);
        var                 p       = BuildFilterParams(request);

        IEnumerable<SeriesAggRow>   seriesAgg;
        IEnumerable<DepartureRow>   departures;
        IEnumerable<GroupTaskRow>   allTasks;
        BusinessRulesRow?           rules;

        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            seriesAgg  = await conn.QueryAsync<SeriesAggRow>(
                OpsGroupsDbHelper.SeriesAggregateSql(db, filters),
                p,
                commandTimeout: 15);

            departures = await conn.QueryAsync<DepartureRow>(
                OpsGroupsDbHelper.DeparturesListSql(db, filters, 0, 5000),
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

        string readinessMethod      = rules?.ReadinessMethod      ?? "required_only";
        bool   includeNaInReadiness = rules?.IncludeNaInReadiness ?? false;
        var tasksByDep  = allTasks.GroupBy(t => t.DepartureId).ToDictionary(g => g.Key, g => g.ToList());
        var depBySeries = departures.GroupBy(d => d.SeriesCode).ToDictionary(g => g.Key, g => g.ToList());

        var series = seriesAgg.Select(s =>
        {
            var deps = depBySeries.TryGetValue(s.SeriesCode, out var d) ? d : new();
            int greenCount = 0, amberCount = 0, redCount = 0;
            int completeCount = 0, inProgressCount = 0, overdueCount = 0;

            foreach (var dep in deps)
            {
                var tasks     = tasksByDep.TryGetValue(dep.DepartureId, out var t) ? t : new();
                int readiness = OpsGroupsDbHelper.ComputeReadinessPct(tasks, readinessMethod, includeNaInReadiness);
                string risk   = OpsGroupsDbHelper.ComputeRiskLevel(readiness);
                if (risk == "green") greenCount++;
                else if (risk == "amber") amberCount++;
                else redCount++;

                completeCount   += tasks.Count(t2 => t2.Status == "complete");
                inProgressCount += tasks.Count(t2 => t2.Status == "in_progress");
                overdueCount    += tasks.Count(t2 => t2.Status == "overdue");
            }

            return new
            {
                series_code  = s.SeriesCode,
                series_name  = s.SeriesName,
                totals = new
                {
                    pax        = s.TotalPax,
                    departures = s.TotalDepartures,
                    risk_counts = new { green = greenCount, amber = amberCount, red = redCount },
                    task_counts = new { complete = completeCount, in_progress = inProgressCount, overdue = overdueCount },
                },
                departures = deps.Select(dep =>
                {
                    var tasks     = tasksByDep.TryGetValue(dep.DepartureId, out var t) ? t : new();
                    int readiness = OpsGroupsDbHelper.ComputeReadinessPct(tasks, readinessMethod, includeNaInReadiness);
                    return new
                    {
                        departure_id     = dep.DepartureId,
                        departure_date   = dep.DepartureDate,
                        destination_name = dep.DestinationName,
                        pax_count        = dep.PaxCount,
                        readiness_pct    = readiness,
                        risk_level       = OpsGroupsDbHelper.ComputeRiskLevel(readiness),
                    };
                }).ToList(),
            };
        }).ToList();

        return TypedResults.Ok(new { series });
    }

    // -------------------------------------------------------------------------
    // Heatmap
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleHeatmapAsync(
        FilterRequest                    request,
        HttpContext                      httpContext,
        IDbConnectionFactory             connectionFactory,
        IOptions<OpsGroupsDbSettings>    opsGroupsDbOptions,
        CancellationToken                ct)
    {
        var check = ValidateTenant(request, httpContext);
        if (check == "validation") return TypedResults.ValidationProblem(RequestContextValidator.Validate(request), title: "Validation failed");
        if (check == "forbidden")  return TypedResults.Problem(title: "Forbidden", detail: "tenant_id does not match the authenticated tenant.", statusCode: StatusCodes.Status403Forbidden);

        OpsGroupsDbSettings db           = opsGroupsDbOptions.Value;
        DateOnly            windowStart  = request.WindowStart ?? DateOnly.FromDateTime(DateTime.UtcNow);
        DepartureFilters    filters      = ToFilters(request) with { WindowStart = windowStart };
        var                 p            = BuildFilterParams(request);
        p.Add("WindowStart", windowStart);

        IEnumerable<DateOnly>    dates;
        IEnumerable<DepartureRow> departures;
        IEnumerable<GroupTaskRow> allTasks;
        BusinessRulesRow?         rules;

        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            dates = await conn.QueryAsync<DateOnly>(
                OpsGroupsDbHelper.HeatmapDatesSql(db, filters),
                p,
                commandTimeout: 10);

            var depFilters = ToFilters(request);
            departures = await conn.QueryAsync<DepartureRow>(
                OpsGroupsDbHelper.DeparturesListSql(db, depFilters, 0, 5000),
                BuildFilterParams(request),
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

        string readinessMethod      = rules?.ReadinessMethod      ?? "required_only";
        bool   includeNaInReadiness = rules?.IncludeNaInReadiness ?? false;
        var dateList   = dates.Take(28).ToList();
        var tasksByDep = allTasks.GroupBy(t => t.DepartureId).ToDictionary(g => g.Key, g => g.ToList());

        var depsBySeries = departures.GroupBy(d => d.SeriesCode)
                                     .OrderBy(g => g.Key)
                                     .Select(g => new
                                     {
                                         series_code = g.Key,
                                         series_name = g.First().SeriesName,
                                         deps        = g.ToList(),
                                     }).ToList();

        var rows = depsBySeries.Select(s =>
        {
            var cells = dateList.Select(date =>
            {
                var dep = s.deps.FirstOrDefault(d => d.DepartureDate == date);
                if (dep is null)
                    return (object)new { date, departure_id = (string?)null, readiness_pct = (int?)null, risk_level = (string?)null };

                var tasks     = tasksByDep.TryGetValue(dep.DepartureId, out var t) ? t : new();
                int readiness = OpsGroupsDbHelper.ComputeReadinessPct(tasks, readinessMethod, includeNaInReadiness);
                return (object)new
                {
                    date,
                    departure_id  = dep.DepartureId,
                    readiness_pct = readiness,
                    risk_level    = OpsGroupsDbHelper.ComputeRiskLevel(readiness),
                };
            }).ToList();

            return new { s.series_code, s.series_name, cells };
        }).ToList();

        return TypedResults.Ok(new
        {
            dates        = dateList,
            window_start = windowStart,
            has_prev     = windowStart > (request.DateFrom ?? DateOnly.MinValue),
            has_next     = dateList.Count == 28,
            rows,
        });
    }

    // -------------------------------------------------------------------------
    // Inline DTOs
    // -------------------------------------------------------------------------
    private sealed record FacetRow(
        string BranchCode,
        string OpsManagerInitials,
        string OpsManagerName,
        string OpsExecInitials,
        string OpsExecName,
        string SeriesCode,
        string SeriesName);

    private sealed record FacetsTourGenericRow(string TourGenericCode, string TourGenericName);

    private sealed record SeriesAggRow(
        string SeriesCode,
        string SeriesName,
        int    TotalPax,
        int    TotalDepartures);
}
