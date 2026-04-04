using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Models;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// List by client: <c>POST /api/v1/todos/list/by-client</c>
/// Required: account_code_client (leading zeros are significant — treat as string).
/// Optional: done_ind, due_date range, include_frozen.
/// </summary>
public static class ListByClientEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/list/by-client", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoListByClient");
    }

    private static async Task<IResult> HandleAsync(
        ListByClientRequest  request,
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

        var domainErrors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.AccountCodeClient))
            domainErrors["account_code_client"] = ["account_code_client is required."];
        if (request.PageNo < 1 || request.PageSize < 1 || request.PageSize > 200)
            domainErrors["page"] = ["page_no must be >= 1 and page_size must be between 1 and 200."];
        if (domainErrors.Count > 0)
            return TypedResults.ValidationProblem(domainErrors, title: "Validation failed");

        // TODO: rights check — user must have list rights for ToDo

        string todo     = dialect.TableRef("sales97", "ToDo");
        string priority = dialect.TableRef("sales97", "Priority");
        string users    = dialect.TableRef("sales97", "users");
        string acctMast = dialect.TableRef("sales97", "accountmast");

        var conditions = new List<string> { "t.Accountcode_Client = @AccountCodeClient" };
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
                request.AccountCodeClient,
                DueDateFrom = request.DueDateFrom.HasValue ? (DateTime?)request.DueDateFrom.Value.ToDateTime(TimeOnly.MinValue) : null,
                DueDateTo   = request.DueDateTo.HasValue   ? (DateTime?)request.DueDateTo.Value.ToDateTime(TimeOnly.MinValue)   : null,
            },
            commandTimeout: 30);

        IEnumerable<ToDoListItem>     items = rows.Select(ToDoListProjections.Project);
        ToDoPagedResult<ToDoListItem> page  = ToDoListProjections.BuildPage(items, request.PageNo, request.PageSize);

        return TypedResults.Ok(page);
    }

    private sealed record ListByClientRequest : RequestContext
    {
        public string   AccountCodeClient { get; init; } = string.Empty;
        public bool?    DoneInd           { get; init; }
        public DateOnly? DueDateFrom      { get; init; }
        public DateOnly? DueDateTo        { get; init; }
        public bool     IncludeFrozen     { get; init; }
        public int      PageNo            { get; init; } = 1;
        public int      PageSize          { get; init; } = 50;
    }
}
