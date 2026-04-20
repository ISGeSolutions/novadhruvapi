using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Models;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// List by assignee: <c>POST /api/v1/todos/list/by-assignee</c>
/// Required: assigned_to_user_code.
/// Optional: done_ind filter (true/false/null = both), due_date range, include_frozen.
/// </summary>
public static class ListByAssigneeEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/list/by-assignee", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoListByAssignee");
    }

    private static async Task<IResult> HandleAsync(
        ListByAssigneeRequest request,
        TenantContext         tenantContext,
        IDbConnectionFactory  connectionFactory,
        ISqlDialect           dialect,
        CancellationToken     ct)
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

        if (request.PageNo < 1 || request.PageSize < 1 || request.PageSize > 200)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["page"] = ["page_no must be >= 1 and page_size must be between 1 and 200."] },
                title: "Validation failed");

        // TODO: rights check — user must have list rights for ToDo

        string todo      = dialect.TableRef("sales97", "ToDo");
        string priority  = dialect.TableRef("sales97", "Priority");
        string users     = dialect.TableRef("sales97", "users");
        string acctMast  = dialect.TableRef("sales97", "accountmast");

        var conditions = new List<string> { "t.AssignedToUserCode = @AssignedToUserCode" };
        if (!request.IncludeFrozen) conditions.Add($"t.FrzInd = {dialect.BooleanLiteral(false)}");
        if (request.DoneInd.HasValue)     conditions.Add($"t.DoneInd = {dialect.BooleanLiteral(request.DoneInd.Value)}");
        if (request.DueDateFrom.HasValue) conditions.Add("CONVERT(date, t.DueDate) >= @DueDateFrom");  // TODO (item 5): MSSQL-only
        if (request.DueDateTo.HasValue)   conditions.Add("CONVERT(date, t.DueDate) <= @DueDateTo");

        string where         = string.Join(" AND ", conditions);
        int    skip          = (request.PageNo - 1) * request.PageSize;
        int    fetch         = request.PageSize + 1;
        string offsetFetch   = dialect.OffsetFetchClause(skip, fetch);

        // JOIN: table refs are dialect-resolved. Column names (description, fullname, accountname)
        // assume the legacy schema shape — confirm against actual schema before go-live.
        // TODO (item 7): cross-database JOINs to sales97.* are MSSQL-only — adapt for Postgres/MariaDB.
        // TODO (item 5): CONVERT(date, ...) in WHERE is MSSQL-only — use CAST(... AS DATE) for Postgres/MariaDB.
        // MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by rajeevjha on 14 Apr 2026.
        string sql = $"""
            SELECT
                t.SeqNo, t.PriorityCode, t.StartDate, t.DueDate, t.AssignedToUserCode,
                t.TaskDetail, t.Remark, t.CreatedBy, t.CreatedOn, t.UpdatedBy, t.UpdatedOn,
                t.SendSMSInd, t.SendSMSTo, t.SentMailInd, t.DoneInd,
                t.Accountcode_Client, t.BkgNo, t.QuoteNo, ISNULL(t.FrzInd, 0) AS frz_ind,
                p.description  AS PriorityName,
                u.fullname     AS AssignedToUserName,
                uc.fullname    AS CreatedByName,
                uu.fullname    AS UpdatedByName,
                a.accountname  AS ClientName,
                NULL           AS TourCode,
                NULL           AS ItineraryName
            FROM {todo} t
            LEFT JOIN {priority}  p  ON p.code        = t.PriorityCode
            LEFT JOIN {users}     u  ON u.code        = t.AssignedToUserCode
            LEFT JOIN {users}     uc ON uc.code       = t.CreatedBy
            LEFT JOIN {users}     uu ON uu.code       = t.UpdatedBy
            LEFT JOIN {acctMast}  a  ON a.accountcode = t.Accountcode_Client
            WHERE {where}
            ORDER BY t.DueDate ASC, t.PriorityCode ASC
            {offsetFetch}
            """;

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        IEnumerable<ToDoListRow> rows = await connection.QueryAsync<ToDoListRow>(
            sql,
            new
            {
                request.AssignedToUserCode,
                DueDateFrom = request.DueDateFrom.HasValue ? (DateTime?)request.DueDateFrom.Value.ToDateTime(TimeOnly.MinValue) : null,
                DueDateTo   = request.DueDateTo.HasValue   ? (DateTime?)request.DueDateTo.Value.ToDateTime(TimeOnly.MinValue)   : null,
            },
            commandTimeout: 30);

        IEnumerable<ToDoListItem>     items = rows.Select(ToDoListProjections.Project);
        ToDoPagedResult<ToDoListItem> page  = ToDoListProjections.BuildPage(items, request.PageNo, request.PageSize);

        return TypedResults.Ok(page);
    }

    private sealed record ListByAssigneeRequest : RequestContext
    {
        public string    AssignedToUserCode { get; init; } = string.Empty;
        public bool?     DoneInd            { get; init; }
        public DateOnly? DueDateFrom        { get; init; }
        public DateOnly? DueDateTo          { get; init; }
        public bool      IncludeFrozen      { get; init; }
        public int       PageNo             { get; init; } = 1;
        public int       PageSize           { get; init; } = 50;
    }
}
