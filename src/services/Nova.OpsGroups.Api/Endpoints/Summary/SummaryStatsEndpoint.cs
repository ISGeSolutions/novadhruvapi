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

        // Fetch business rules so readiness method and N/A flag are respected.
        BusinessRulesRow? rules;
        SummaryStatsRow   stats;
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
                    DateFrom = request.DateFrom ?? today,
                    DateTo   = request.DateTo   ?? today.AddDays(365),
                    Today    = today,
                    WeekEnd  = weekEnd,
                    TodayUtc = todayUtc,
                },
                commandTimeout: 15);
        }

        string readinessMethod      = rules?.ReadinessMethod      ?? "required_only";
        bool   includeNaInReadiness = rules?.IncludeNaInReadiness ?? false;

        // TODO: compute readiness_avg_pct across all departures in the date window.
        // The SummaryStatsSql correlated-subquery path does not return per-departure task data,
        // so avg readiness cannot be derived from `stats` alone.
        // Options:
        //   A) Fetch departures + tasks (same as DashboardEndpoints.HandleSummaryAsync) and
        //      average ComputeReadinessPct(tasks, readinessMethod, includeNaInReadiness) per departure.
        //   B) Add a readiness-avg subquery to SummaryStatsSql (complex; requires task join).
        // Option A is simplest — copy the departure+task fetch block from DashboardEndpoints.HandleSummaryAsync,
        // then: readinessAvg = departures.Average(dep => ComputeReadinessPct(tasksByDep[dep], ...))
        double readinessAvg = 0; // replace with computed value

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
