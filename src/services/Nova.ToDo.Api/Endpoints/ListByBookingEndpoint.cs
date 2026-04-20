using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Models;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// List by booking: <c>POST /api/v1/todos/list/by-booking</c>
/// Required: bkg_no.
/// Optional: done_ind, include_frozen.
/// </summary>
public static class ListByBookingEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/list/by-booking", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoListByBooking");
    }

    private static async Task<IResult> HandleAsync(
        ListByBookingRequest request,
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
        if (request.BkgNo <= 0)
            domainErrors["bkg_no"] = ["bkg_no must be a positive integer."];
        if (request.PageNo < 1 || request.PageSize < 1 || request.PageSize > 200)
            domainErrors["page"] = ["page_no must be >= 1 and page_size must be between 1 and 200."];
        if (domainErrors.Count > 0)
            return TypedResults.ValidationProblem(domainErrors, title: "Validation failed");

        // TODO: rights check — user must have list rights for ToDo

        string todo     = dialect.TableRef("sales97", "ToDo");
        string priority = dialect.TableRef("sales97", "Priority");
        string users    = dialect.TableRef("sales97", "users");
        string acctMast = dialect.TableRef("sales97", "accountmast");

        var conditions = new List<string> { "t.BkgNo = @BkgNo" };
        if (!request.IncludeFrozen)   conditions.Add($"t.FrzInd = {dialect.BooleanLiteral(false)}");
        if (request.DoneInd.HasValue) conditions.Add($"t.DoneInd = {dialect.BooleanLiteral(request.DoneInd.Value)}");

        string where       = string.Join(" AND ", conditions);
        int    skip        = (request.PageNo - 1) * request.PageSize;
        int    fetch       = request.PageSize + 1;
        string offsetFetch = dialect.OffsetFetchClause(skip, fetch);

        // TODO (item 7): cross-database JOINs are MSSQL-only — adapt for Postgres/MariaDB.
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
                /* TODO: JOIN fit.dbo.bookingdetail for TourCode and ItineraryName */
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
            sql, new { request.BkgNo }, commandTimeout: 30);

        IEnumerable<ToDoListItem>     items = rows.Select(ToDoListProjections.Project);
        ToDoPagedResult<ToDoListItem> page  = ToDoListProjections.BuildPage(items, request.PageNo, request.PageSize);

        return TypedResults.Ok(page);
    }

    private sealed record ListByBookingRequest : RequestContext
    {
        public int   BkgNo         { get; init; }
        public bool? DoneInd       { get; init; }
        public bool  IncludeFrozen { get; init; }
        public int   PageNo        { get; init; } = 1;
        public int   PageSize      { get; init; } = 50;
    }
}
