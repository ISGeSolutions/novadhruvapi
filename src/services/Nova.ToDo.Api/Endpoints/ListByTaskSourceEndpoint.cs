using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Models;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// List by task-source: <c>POST /api/v1/todos/list/by-task-source</c>
/// Exactly one task-source field must be populated. Returns 400 if zero or more than one.
/// Optional: done_ind, due_date range, include_frozen.
/// </summary>
public static class ListByTaskSourceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/list/by-task-source", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoListByTaskSource");
    }

    private static async Task<IResult> HandleAsync(
        ListByTaskSourceRequest request,
        TenantContext           tenantContext,
        IDbConnectionFactory    connectionFactory,
        ISqlDialect             dialect,
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

        int populated = CountPopulated(request);
        if (populated == 0)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["task_source"] = ["Exactly one task-source field must be provided: travel_pnr_no, seq_no_charges, seq_no_acct_notes, or itinerary_no."]
                },
                title: "Validation failed");

        if (populated > 1)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["task_source"] = ["Only one task-source field may be provided at a time."]
                },
                title: "Validation failed");

        if (request.PageNo < 1 || request.PageSize < 1 || request.PageSize > 200)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["page"] = ["page_no must be >= 1 and page_size must be between 1 and 200."] },
                title: "Validation failed");

        // TODO: rights check — user must have list rights for ToDo

        string todo     = dialect.TableRef("sales97", "ToDo");
        string priority = dialect.TableRef("sales97", "Priority");
        string users    = dialect.TableRef("sales97", "users");
        string acctMast = dialect.TableRef("sales97", "accountmast");

        string taskSourceCondition = BuildTaskSourceCondition(request);
        var conditions = new List<string> { taskSourceCondition };
        if (!request.IncludeFrozen)       conditions.Add($"t.frz_ind = {dialect.BooleanLiteral(false)}");
        if (request.DoneInd.HasValue)     conditions.Add($"t.done_ind = {dialect.BooleanLiteral(request.DoneInd.Value)}");
        if (request.DueDateFrom.HasValue) conditions.Add("CONVERT(date, t.DueDate) >= @DueDateFrom");  // TODO (item 5)
        if (request.DueDateTo.HasValue)   conditions.Add("CONVERT(date, t.DueDate) <= @DueDateTo");

        string where       = string.Join(" AND ", conditions);
        int    skip        = (request.PageNo - 1) * request.PageSize;
        int    fetch       = request.PageSize + 1;
        string offsetFetch = dialect.OffsetFetchClause(skip, fetch);

        // TODO (item 7): cross-database JOINs are MSSQL-only — adapt for Postgres/MariaDB.
        string sql = $"""
            SELECT
                t.SeqNo, t.PriorityCode, t.StartDate, t.DueDate, t.AssignedToUserCode,
                t.TaskDetail, t.Remark, t.CreatedBy, t.CreatedOn, t.UpdatedBy, t.UpdatedOn,
                t.SendSMSInd, t.SendSMSTo, t.SentMailInd, t.DoneInd,
                t.Accountcode_Client, t.BkgNo, t.QuoteNo, t.FrzInd,
                p.description  AS PriorityName,
                u.fullname     AS AssignedToUserName,
                uc.fullname    AS CreatedByName,
                uu.fullname    AS UpdatedByName,
                a.accountname  AS ClientName,
                NULL           AS TourCode,
                NULL           AS ItineraryName
            FROM {todo} t
            LEFT JOIN {priority} p  ON p.code        = t.PriorityCode
            LEFT JOIN {users}    u  ON u.code        = t.AssignedToUserCode
            LEFT JOIN {users}    uc ON uc.code       = t.CreatedBy
            LEFT JOIN {users}    uu ON uu.code       = t.UpdatedBy
            LEFT JOIN {acctMast} a  ON a.accountcode = t.Accountcode_Client
            WHERE {where}
            ORDER BY t.DueDate ASC
            {offsetFetch}
            """;

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        IEnumerable<ToDoListRow> rows = await connection.QueryAsync<ToDoListRow>(
            sql,
            new
            {
                TravelPrnNo    = request.TravelPrnNo,
                SeqNoCharges   = request.SeqNoCharges,
                SeqNoAcctNotes = request.SeqNoAcctNotes,
                ItineraryNo    = request.ItineraryNo,
                DueDateFrom    = request.DueDateFrom.HasValue ? (DateTime?)request.DueDateFrom.Value.ToDateTime(TimeOnly.MinValue) : null,
                DueDateTo      = request.DueDateTo.HasValue   ? (DateTime?)request.DueDateTo.Value.ToDateTime(TimeOnly.MinValue)   : null,
            },
            commandTimeout: 30);

        IEnumerable<ToDoListItem>     items = rows.Select(ToDoListProjections.Project);
        ToDoPagedResult<ToDoListItem> page  = ToDoListProjections.BuildPage(items, request.PageNo, request.PageSize);

        return TypedResults.Ok(page);
    }

    private static int CountPopulated(ListByTaskSourceRequest r) =>
        (r.TravelPrnNo    is not null ? 1 : 0) +
        (r.SeqNoCharges   is not null ? 1 : 0) +
        (r.SeqNoAcctNotes is not null ? 1 : 0) +
        (r.ItineraryNo    is not null ? 1 : 0);

    private static string BuildTaskSourceCondition(ListByTaskSourceRequest r) =>
        r.TravelPrnNo    is not null ? "t.Travel_PNRNo   = @TravelPrnNo"    :
        r.SeqNoCharges   is not null ? "t.SeqNo_Charges  = @SeqNoCharges"   :
        r.SeqNoAcctNotes is not null ? "t.SeqNo_AcctNotes = @SeqNoAcctNotes" :
                                       "t.Itinerary_No   = @ItineraryNo";

    private sealed record ListByTaskSourceRequest : RequestContext
    {
        public string?   TravelPrnNo    { get; init; }
        public int?      SeqNoCharges   { get; init; }
        public int?      SeqNoAcctNotes { get; init; }
        public int?      ItineraryNo    { get; init; }
        public bool?     DoneInd        { get; init; }
        public DateOnly? DueDateFrom    { get; init; }
        public DateOnly? DueDateTo      { get; init; }
        public bool      IncludeFrozen  { get; init; }
        public int       PageNo         { get; init; } = 1;
        public int       PageSize       { get; init; } = 50;
    }
}
