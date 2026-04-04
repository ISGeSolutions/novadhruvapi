using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Models;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// Pre-edit Get: <c>POST /api/v1/todos/by-booking</c>
/// Returns the first open ToDo for a given booking and job code.
/// Does NOT filter on frz_ind. Filters on done_ind = false.
/// </summary>
public static class GetByBookingEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/by-booking", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoGetByBooking");
    }

    private static async Task<IResult> HandleAsync(
        GetByBookingRequest  request,
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
        if (string.IsNullOrWhiteSpace(request.JobCode))
            domainErrors["job_code"] = ["job_code is required."];
        if (domainErrors.Count > 0)
            return TypedResults.ValidationProblem(domainErrors, title: "Validation failed");

        // TODO: rights check — user must have read rights for ToDo

        string todo      = dialect.TableRef("sales97", "ToDo");
        string doneOff   = dialect.BooleanLiteral(false);

        // Returns most recent open task for this booking + job code.
        // TODO (item 2): SELECT TOP 1 is MSSQL-only — replace with LIMIT 1 for Postgres/MariaDB.
        // Does not filter on frz_ind. done_ind = false (open tasks only).
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
            WHERE BkgNo  = @BkgNo
              AND JobCode = @JobCode
              AND DoneInd = {doneOff}
            ORDER BY SeqNo DESC
            """;

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        ToDoRow? row = await connection.QuerySingleOrDefaultAsync<ToDoRow>(
            sql, new { request.BkgNo, request.JobCode }, commandTimeout: 30);

        if (row is null)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     $"No open ToDo found for bkg_no {request.BkgNo} with job_code '{request.JobCode}'.",
                statusCode: StatusCodes.Status404NotFound);

        return TypedResults.Ok(ToDoProjections.Project(row));
    }

    private sealed record GetByBookingRequest : RequestContext
    {
        public int    BkgNo   { get; init; }
        public string JobCode { get; init; } = string.Empty;
    }
}
