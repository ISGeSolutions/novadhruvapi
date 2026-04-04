using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// Aggregate: <c>POST /api/v1/todos/summary/by-user</c>
/// Returns task counts for a given assignee, using tenant timezone to derive "today".
/// Excludes frozen records (frz_ind = 0).
/// </summary>
public static class SummaryByUserEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/summary/by-user", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoSummaryByUser");
    }

    private static async Task<IResult> HandleAsync(
        SummaryByUserRequest request,
        TenantContext        tenantContext,
        IDbConnectionFactory connectionFactory,
        ISqlDialect          dialect,
        CancellationToken    ct)
    {
        Dictionary<string, string[]> contextErrors = RequestContextValidator.Validate(request);
        if (contextErrors.Count > 0)
            return TypedResults.ValidationProblem(contextErrors, title: "Validation failed");

        if (!RequestContextValidator.TenantMatches(request, tenantContext))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id in the request body does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        if (string.IsNullOrWhiteSpace(request.AssignedToUserCode))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["assigned_to_user_code"] = ["assigned_to_user_code is required."] },
                title: "Validation failed");

        // TODO: rights check — user may only view summary for themselves or requires supervisor rights

        // Derive UTC "today" boundaries from tenant timezone (browser_timezone from request context).
        // browser_timezone is an IANA timezone string (e.g. "Europe/London").
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(request.BrowserTimezone ?? "UTC");
        }
        catch
        {
            tz = TimeZoneInfo.Utc;
        }

        DateTime localNow    = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        DateTime localToday  = localNow.Date;
        DateTime todayUtcStart = TimeZoneInfo.ConvertTimeToUtc(localToday, tz);
        DateTime todayUtcEnd   = TimeZoneInfo.ConvertTimeToUtc(localToday.AddDays(1).AddTicks(-1), tz);

        string todo = dialect.TableRef("sales97", "ToDo");
        string frzOff = dialect.BooleanLiteral(false);
        string doneOff = dialect.BooleanLiteral(false);
        string doneOn  = dialect.BooleanLiteral(true);

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        // All aggregate queries filter: frz_ind = false, AssignedToUserCode = @UserCode
        // TODO (item 5): DueDate/DoneOn/CreatedOn comparisons assume datetime — review CAST for Postgres/MariaDB.
        string sql = $"""
            SELECT
                -- Due today, by priority
                SUM(CASE WHEN DoneInd = {doneOff} AND DueDate >= @TodayStart AND DueDate <= @TodayEnd AND PriorityCode = 'H' THEN 1 ELSE 0 END) AS DueTodayHigh,
                SUM(CASE WHEN DoneInd = {doneOff} AND DueDate >= @TodayStart AND DueDate <= @TodayEnd AND PriorityCode = 'N' THEN 1 ELSE 0 END) AS DueTodayNormal,
                SUM(CASE WHEN DoneInd = {doneOff} AND DueDate >= @TodayStart AND DueDate <= @TodayEnd AND PriorityCode = 'L' THEN 1 ELSE 0 END) AS DueTodayLow,
                -- Overdue (open, due before today), by priority
                SUM(CASE WHEN DoneInd = {doneOff} AND DueDate < @TodayStart AND PriorityCode = 'H' THEN 1 ELSE 0 END) AS OverdueHigh,
                SUM(CASE WHEN DoneInd = {doneOff} AND DueDate < @TodayStart AND PriorityCode = 'N' THEN 1 ELSE 0 END) AS OverdueNormal,
                SUM(CASE WHEN DoneInd = {doneOff} AND DueDate < @TodayStart AND PriorityCode = 'L' THEN 1 ELSE 0 END) AS OverdueLow,
                -- WIP: open with StartDate set
                SUM(CASE WHEN DoneInd = {doneOff} AND StartDate IS NOT NULL THEN 1 ELSE 0 END) AS WipCount,
                -- Due today AND created today
                SUM(CASE WHEN DoneInd = {doneOff} AND DueDate >= @TodayStart AND DueDate <= @TodayEnd
                         AND CreatedOn >= @TodayStart AND CreatedOn <= @TodayEnd THEN 1 ELSE 0 END) AS DueTodayCreatedToday,
                -- Completed today
                SUM(CASE WHEN DoneInd = {doneOn} AND DoneOn >= @TodayStart AND DoneOn <= @TodayEnd THEN 1 ELSE 0 END) AS CompletedToday
            FROM {todo}
            WHERE FrzInd = {frzOff}
              AND AssignedToUserCode = @AssignedToUserCode
            """;

        SummaryRow row = await connection.QuerySingleAsync<SummaryRow>(
            sql,
            new
            {
                request.AssignedToUserCode,
                TodayStart = todayUtcStart,
                TodayEnd   = todayUtcEnd,
            },
            commandTimeout: 30);

        return TypedResults.Ok(new
        {
            assigned_to_user_code = request.AssignedToUserCode,
            due_today = new
            {
                high   = row.DueTodayHigh,
                normal = row.DueTodayNormal,
                low    = row.DueTodayLow,
                total  = row.DueTodayHigh + row.DueTodayNormal + row.DueTodayLow,
            },
            overdue = new
            {
                high   = row.OverdueHigh,
                normal = row.OverdueNormal,
                low    = row.OverdueLow,
                total  = row.OverdueHigh + row.OverdueNormal + row.OverdueLow,
            },
            wip_count              = row.WipCount,
            due_today_created_today = row.DueTodayCreatedToday,
            completed_today        = row.CompletedToday,
        });
    }

    private sealed record SummaryRow(
        int DueTodayHigh, int DueTodayNormal, int DueTodayLow,
        int OverdueHigh,  int OverdueNormal,  int OverdueLow,
        int WipCount, int DueTodayCreatedToday, int CompletedToday);

    private sealed record SummaryByUserRequest : RequestContext
    {
        public string AssignedToUserCode { get; init; } = string.Empty;
    }
}
