using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// Aggregate: <c>POST /api/v1/todos/summary/by-context</c>
/// Exactly one context group must be supplied:
///   booking_no, quote_no, account_code_client, supplier_code,
///   OR (tour_series_code + dep_date).
/// Returns 400 if zero or more than one group is populated.
/// Excludes frozen records (frz_ind = 0).
/// </summary>
public static class SummaryByContextEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/summary/by-context", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoSummaryByContext");
    }

    private static async Task<IResult> HandleAsync(
        SummaryByContextRequest request,
        TenantContext           tenantContext,
        IDbConnectionFactory    connectionFactory,
        ISqlDialect             dialect,
        IConfiguration          configuration,
        CancellationToken       ct)
    {
        Dictionary<string, string[]> contextErrors = RequestContextValidator.Validate(request);
        if (contextErrors.Count > 0)
            return TypedResults.ValidationProblem(contextErrors, title: "Validation failed");

        if (!RequestContextValidator.TenantMatches(request, tenantContext))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id in the request body does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        // Exactly one context group
        bool hasBkgNo       = request.BookingNo.HasValue;
        bool hasQuoteNo     = request.QuoteNo.HasValue;
        bool hasClient      = !string.IsNullOrWhiteSpace(request.AccountCodeClient);
        bool hasSupplier    = !string.IsNullOrWhiteSpace(request.SupplierCode);
        bool hasTourSeries  = !string.IsNullOrWhiteSpace(request.TourSeriesCode) && request.DepDate.HasValue;

        int groupCount = (hasBkgNo ? 1 : 0) + (hasQuoteNo ? 1 : 0) + (hasClient ? 1 : 0)
                       + (hasSupplier ? 1 : 0) + (hasTourSeries ? 1 : 0);

        if (groupCount == 0)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["context"] = ["Exactly one context must be provided: booking_no, quote_no, account_code_client, supplier_code, or (tour_series_code + dep_date)."]
                },
                title: "Validation failed");

        if (groupCount > 1)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["context"] = ["Only one context group may be provided at a time."]
                },
                title: "Validation failed");

        // TODO: rights check

        // Derive UTC "today" boundaries from tenant timezone
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(request.BrowserTimezone ?? "UTC");
        }
        catch
        {
            tz = TimeZoneInfo.Utc;
        }

        DateTime localNow      = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        DateTime todayUtcStart = TimeZoneInfo.ConvertTimeToUtc(localNow.Date, tz);
        DateTime todayUtcEnd   = TimeZoneInfo.ConvertTimeToUtc(localNow.Date.AddDays(1).AddTicks(-1), tz);

        // Config-driven windows for completed count scope
        int clientWindowDays   = configuration.GetValue("ToDoSummary:ClientCompletedWindowDays",   15);
        int supplierWindowDays = configuration.GetValue("ToDoSummary:SupplierCompletedWindowDays", 30);

        string todo   = dialect.TableRef("sales97", "ToDo");
        string frzOff  = dialect.BooleanLiteral(false);
        string doneOff = dialect.BooleanLiteral(false);
        string doneOn  = dialect.BooleanLiteral(true);

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        string contextFilter   = BuildContextFilter(request, hasBkgNo, hasQuoteNo, hasClient, hasSupplier, hasTourSeries);
        string completedFilter = BuildCompletedFilter(hasBkgNo, hasQuoteNo, hasTourSeries, hasClient, hasSupplier,
            todayUtcStart, clientWindowDays, supplierWindowDays);

        // TODO (item 5): DueDate/DoneOn/CreatedOn comparisons assume datetime — review CAST for Postgres/MariaDB.
        string sql = $"""
            SELECT
                SUM(CASE WHEN DoneInd = {doneOff} AND DueDate >= @TodayStart AND DueDate <= @TodayEnd AND PriorityCode = 'H' THEN 1 ELSE 0 END) AS DueTodayHigh,
                SUM(CASE WHEN DoneInd = {doneOff} AND DueDate >= @TodayStart AND DueDate <= @TodayEnd AND PriorityCode = 'N' THEN 1 ELSE 0 END) AS DueTodayNormal,
                SUM(CASE WHEN DoneInd = {doneOff} AND DueDate >= @TodayStart AND DueDate <= @TodayEnd AND PriorityCode = 'L' THEN 1 ELSE 0 END) AS DueTodayLow,
                SUM(CASE WHEN DoneInd = {doneOff} AND DueDate < @TodayStart AND PriorityCode = 'H' THEN 1 ELSE 0 END) AS OverdueHigh,
                SUM(CASE WHEN DoneInd = {doneOff} AND DueDate < @TodayStart AND PriorityCode = 'N' THEN 1 ELSE 0 END) AS OverdueNormal,
                SUM(CASE WHEN DoneInd = {doneOff} AND DueDate < @TodayStart AND PriorityCode = 'L' THEN 1 ELSE 0 END) AS OverdueLow,
                SUM(CASE WHEN DoneInd = {doneOff} AND StartDate IS NOT NULL THEN 1 ELSE 0 END) AS WipCount,
                SUM(CASE WHEN DoneInd = {doneOff} AND DueDate >= @TodayStart AND DueDate <= @TodayEnd
                         AND CreatedOn >= @TodayStart AND CreatedOn <= @TodayEnd THEN 1 ELSE 0 END) AS DueTodayCreatedToday,
                SUM(CASE WHEN DoneInd = {doneOn} {completedFilter} THEN 1 ELSE 0 END) AS CompletedCount
            FROM {todo}
            WHERE FrzInd = {frzOff}
              AND {contextFilter}
            """;

        // account_code_client and supplier_code completed count inline queries differ — see placeholder below.
        // TODO: for account_code_client: join open enquiries/quotes/bookings within 15-day return window.
        // TODO: for supplier_code: filter DoneOn within last 30 days.

        SummaryRow row = await connection.QuerySingleAsync<SummaryRow>(
            sql,
            new
            {
                BookingNo       = request.BookingNo,
                QuoteNo         = request.QuoteNo,
                AccountCodeClient = request.AccountCodeClient,
                SupplierCode    = request.SupplierCode,
                TourSeriesCode  = request.TourSeriesCode,
                DepDate         = request.DepDate.HasValue ? (DateTime?)request.DepDate.Value.ToDateTime(TimeOnly.MinValue) : null,
                TodayStart      = todayUtcStart,
                TodayEnd        = todayUtcEnd,
                SupplierWindowStart = DateTime.UtcNow.AddDays(-supplierWindowDays),
            },
            commandTimeout: 30);

        return TypedResults.Ok(new
        {
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
            wip_count               = row.WipCount,
            due_today_created_today = row.DueTodayCreatedToday,
            completed_count         = row.CompletedCount,
        });
    }

    private static string BuildContextFilter(SummaryByContextRequest r,
        bool hasBkgNo, bool hasQuoteNo, bool hasClient, bool hasSupplier, bool hasTourSeries) =>
        hasBkgNo      ? "BkgNo = @BookingNo"                                                   :
        hasQuoteNo    ? "QuoteNo = @QuoteNo"                                                   :
        hasClient     ? "Accountcode_Client = @AccountCodeClient"                              :
        hasSupplier   ? "SupplierCode = @SupplierCode"                                         :
                        "Brochure_Code_Short = @TourSeriesCode AND CONVERT(date, DepDate) = CONVERT(date, @DepDate)";

    private static string BuildCompletedFilter(
        bool hasBkgNo, bool hasQuoteNo, bool hasTourSeries,
        bool hasClient, bool hasSupplier,
        DateTime todayUtcStart, int clientWindowDays, int supplierWindowDays)
    {
        // booking, quote, tour-series: all completed tasks (all time)
        if (hasBkgNo || hasQuoteNo || hasTourSeries)
            return string.Empty;   // no additional date restriction on completed

        // account_code_client: inline query for open enquiries/quotes/bookings within clientWindowDays
        // TODO: replace NULL with actual inline query scope
        if (hasClient)
            return "/* TODO: AND (inline scope for client — open enquiries, quotes, bookings within 15-day window) */";

        // supplier_code: completed in last supplierWindowDays
        if (hasSupplier)
            return "AND DoneOn >= @SupplierWindowStart";

        return string.Empty;
    }

    private sealed record SummaryRow(
        int DueTodayHigh, int DueTodayNormal, int DueTodayLow,
        int OverdueHigh,  int OverdueNormal,  int OverdueLow,
        int WipCount, int DueTodayCreatedToday, int CompletedCount);

    private sealed record SummaryByContextRequest : RequestContext
    {
        public int?      BookingNo         { get; init; }
        public int?      QuoteNo           { get; init; }
        public string?   AccountCodeClient { get; init; }
        public string?   SupplierCode      { get; init; }
        public string?   TourSeriesCode    { get; init; }
        public DateOnly? DepDate           { get; init; }
    }
}
