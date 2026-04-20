using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Models;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// Pre-edit Get: <c>POST /api/v1/todos/by-quote</c>
/// Returns the first open ToDo for a given quote/enquiry number and job code.
/// Does NOT filter on frz_ind. Filters on done_ind = false.
/// Note: EnquiryNo = QuoteNo in the legacy schema.
/// </summary>
public static class GetByQuoteEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/by-quote", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoGetByQuote");
    }

    private static async Task<IResult> HandleAsync(
        GetByQuoteRequest    request,
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
        if (request.QuoteNo <= 0)
            domainErrors["quote_no"] = ["quote_no must be a positive integer."];
        if (string.IsNullOrWhiteSpace(request.JobCode))
            domainErrors["job_code"] = ["job_code is required."];
        if (domainErrors.Count > 0)
            return TypedResults.ValidationProblem(domainErrors, title: "Validation failed");

        // TODO: rights check — user must have read rights for ToDo

        string todo    = dialect.TableRef("sales97", "ToDo");
        string doneOff = dialect.BooleanLiteral(false);

        // TODO (item 2): SELECT TOP 1 is MSSQL-only — replace with LIMIT 1 for Postgres/MariaDB.
        // MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by rajeevjha on 14 Apr 2026.
        string sql = $"""
            SELECT TOP 1
                SeqNo, JobCode, TaskDetail, AssignedToUserCode, PriorityCode,
                DueDate, FORMAT(DueTime, 'HH:mm') AS due_time, InFlexibleInd,
                StartDate, FORMAT(StartTime, 'HH:mm') AS start_time,
                AssignedByUserCode, AssignedOn, Remark,
                FORMAT(EstJobTime, 'HH:mm') AS est_job_time,
                ClientName, BkgNo, QuoteNo, CampaignCode,
                Accountcode_Client, Brochure_Code_Short AS tour_series_code, DepDate, SupplierCode,
                SendEMailToInd, SentMailInd, AlertToInd, SendSMSInd, SendSMSTo,
                Travel_PNRNo, SeqNo_Charges, SeqNo_AcctNotes, Itinerary_No,
                DoneInd, DoneBy, DoneOn,
                ISNULL(FrzInd, 0) AS frz_ind, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn, UpdatedAt
            FROM {todo}
            WHERE QuoteNo = @QuoteNo
              AND JobCode  = @JobCode
              AND DoneInd  = {doneOff}
            ORDER BY SeqNo DESC
            """;

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        ToDoRow? row = await connection.QuerySingleOrDefaultAsync<ToDoRow>(
            sql, new { request.QuoteNo, request.JobCode }, commandTimeout: 30);

        if (row is null)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     $"No open ToDo found for quote_no {request.QuoteNo} with job_code '{request.JobCode}'.",
                statusCode: StatusCodes.Status404NotFound);

        return TypedResults.Ok(ToDoProjections.Project(row));
    }

    private sealed record GetByQuoteRequest : RequestContext
    {
        public int    QuoteNo { get; init; }
        public string JobCode { get; init; } = string.Empty;
    }
}
