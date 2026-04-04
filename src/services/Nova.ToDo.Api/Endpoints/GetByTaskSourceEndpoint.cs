using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Models;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// Pre-edit Get: <c>POST /api/v1/todos/by-task-source</c>
/// Returns the first open ToDo matching exactly one task-source field.
/// Returns 400 if zero or more than one task-source field is populated.
/// Does NOT filter on frz_ind. Filters on done_ind = false.
/// </summary>
public static class GetByTaskSourceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/by-task-source", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoGetByTaskSource");
    }

    private static async Task<IResult> HandleAsync(
        GetByTaskSourceRequest request,
        TenantContext          tenantContext,
        IDbConnectionFactory   connectionFactory,
        ISqlDialect            dialect,
        CancellationToken      ct)
    {
        Dictionary<string, string[]> contextErrors = RequestContextValidator.Validate(request);
        if (contextErrors.Count > 0)
            return TypedResults.ValidationProblem(contextErrors, title: "Validation failed");

        if (!RequestContextValidator.TenantMatches(request, tenantContext))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id in the request body does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        // Exactly one task-source field must be populated.
        int populatedCount = CountPopulated(request);
        if (populatedCount == 0)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["task_source"] = ["Exactly one task-source field must be provided: travel_pnr_no, seq_no_charges, seq_no_acct_notes, or itinerary_no."]
                },
                title: "Validation failed");

        if (populatedCount > 1)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["task_source"] = ["Only one task-source field may be provided at a time."]
                },
                title: "Validation failed");

        // TODO: rights check — user must have read rights for ToDo

        string todo        = dialect.TableRef("sales97", "ToDo");
        string doneOff     = dialect.BooleanLiteral(false);
        string whereClause = BuildWhereClause(request);

        // TODO (item 2): SELECT TOP 1 is MSSQL-only — replace with LIMIT 1 for Postgres/MariaDB.
        string sql = $"""
            SELECT TOP 1
                SeqNo, JobCode, TaskDetail, AssignedToUserCode, PriorityCode,
                DueDate, DueTime, InFlexibleInd, StartDate, StartTime,
                AssignedByUserCode, AssignedOn, Remark, EstJobTime,
                ClientName, BkgNo, QuoteNo, CampaignCode,
                Accountcode_Client, Brochure_Code_Short, DepDate, SupplierCode,
                SendEMailToInd, SentMailInd, AlertToInd, SendSMSInd, SendSMSTo,
                Travel_PNRNo, SeqNo_Charges, SeqNo_AcctNotes, Itinerary_No,
                DoneInd, DoneBy, DoneOn,
                FrzInd, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn, UpdatedAt
            FROM {todo}
            WHERE {whereClause}
              AND DoneInd = {doneOff}
            ORDER BY SeqNo DESC
            """;

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        ToDoRow? row = await connection.QuerySingleOrDefaultAsync<ToDoRow>(
            sql,
            new
            {
                request.TravelPrnNo,
                request.SeqNoCharges,
                request.SeqNoAcctNotes,
                request.ItineraryNo,
            },
            commandTimeout: 30);

        if (row is null)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     "No open ToDo found for the supplied task-source.",
                statusCode: StatusCodes.Status404NotFound);

        return TypedResults.Ok(ToDoProjections.Project(row));
    }

    private static int CountPopulated(GetByTaskSourceRequest r) =>
        (r.TravelPrnNo    is not null ? 1 : 0) +
        (r.SeqNoCharges   is not null ? 1 : 0) +
        (r.SeqNoAcctNotes is not null ? 1 : 0) +
        (r.ItineraryNo    is not null ? 1 : 0);

    private static string BuildWhereClause(GetByTaskSourceRequest r) =>
        r.TravelPrnNo    is not null ? "Travel_PNRNo    = @TravelPrnNo"    :
        r.SeqNoCharges   is not null ? "SeqNo_Charges   = @SeqNoCharges"   :
        r.SeqNoAcctNotes is not null ? "SeqNo_AcctNotes = @SeqNoAcctNotes" :
                                       "Itinerary_No    = @ItineraryNo";

    private sealed record GetByTaskSourceRequest : RequestContext
    {
        public string? TravelPrnNo    { get; init; }
        public int?    SeqNoCharges   { get; init; }
        public int?    SeqNoAcctNotes { get; init; }
        public int?    ItineraryNo    { get; init; }
    }
}
