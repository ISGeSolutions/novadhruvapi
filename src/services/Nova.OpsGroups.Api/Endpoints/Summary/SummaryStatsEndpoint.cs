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

public static class SummaryStatsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/grouptour-task-summary-stats", HandleAsync)
             .RequireAuthorization()
             .WithName("SummaryStats");
    }

    private static async Task<IResult> HandleAsync(
        StatsRequest                     request,
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

        OpsGroupsDbSettings db        = opsGroupsDbOptions.Value;
        DateOnly            today     = DateOnly.FromDateTime(DateTime.UtcNow);
        DateTimeOffset      todayUtc  = new(DateTime.UtcNow.Date, TimeSpan.Zero);
        DateOnly            weekEnd   = today.AddDays(7);

        DateOnly dateFrom = request.DateFrom ?? today;
        DateOnly dateTo   = request.DateTo   ?? today.AddDays(365);

        BusinessRulesRow?           rules;
        SummaryStatsRow             stats;
        IEnumerable<DepartureRow>   departures;
        IEnumerable<GroupTaskRow>   allTasks;

        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            rules = await conn.QueryFirstOrDefaultAsync<BusinessRulesRow>(
                OpsGroupsDbHelper.BusinessRulesFetchSql(db),
                new { request.TenantId, request.CompanyCode, request.BranchCode },
                commandTimeout: 10);

            stats = await conn.QueryFirstAsync<SummaryStatsRow>(
                OpsGroupsDbHelper.SummaryStatsSql(db),
                new
                {
                    request.TenantId,
                    DateFrom = dateFrom,
                    DateTo   = dateTo,
                    Today    = today,
                    WeekEnd  = weekEnd,
                    TodayUtc = todayUtc,
                },
                commandTimeout: 15);

            var depFilters = new DepartureFilters(dateFrom, dateTo, null, null, null, null, null, null, null);
            var p = new DynamicParameters();
            p.Add("TenantId", request.TenantId);
            p.Add("DateFrom", dateFrom);
            p.Add("DateTo",   dateTo);

            departures = await conn.QueryAsync<DepartureRow>(
                OpsGroupsDbHelper.DeparturesListSql(db, depFilters, 0, 10000),
                p,
                commandTimeout: 15);

            var depIds = departures.Select(d => d.DepartureId).ToList();
            allTasks = depIds.Count == 0
                ? Enumerable.Empty<GroupTaskRow>()
                : await conn.QueryAsync<GroupTaskRow>(
                    OpsGroupsDbHelper.GroupTasksByDepartureIdsSql(db),
                    new { request.TenantId, DepartureIds = depIds },
                    commandTimeout: 15);
        }

        string readinessMethod      = rules?.ReadinessMethod      ?? "required_only";
        bool   includeNaInReadiness = rules?.IncludeNaInReadiness ?? false;

        var tasksByDep = allTasks.GroupBy(t => t.DepartureId).ToDictionary(g => g.Key, g => g.ToList());
        var depList    = departures.ToList();

        double readinessAvg = depList.Count == 0
            ? 0
            : depList.Average(dep =>
                (double)OpsGroupsDbHelper.ComputeReadinessPct(
                    tasksByDep.TryGetValue(dep.DepartureId, out var t) ? t : new(),
                    readinessMethod,
                    includeNaInReadiness));

        return TypedResults.Ok(new
        {
            total_departures    = stats.TotalDepartures,
            overdue_group_tasks = stats.OverdueGroupTasks,
            due_this_week       = stats.DueThisWeek,
            completed_today     = stats.CompletedToday,
            readiness_avg_pct   = readinessAvg,
        });
    }

    private sealed record StatsRequest : RequestContext
    {
        public DateOnly? DateFrom { get; init; }
        public DateOnly? DateTo   { get; init; }
    }

    private sealed record SummaryStatsRow(
        int TotalDepartures,
        int OverdueGroupTasks,
        int DueThisWeek,
        int CompletedToday);
}
