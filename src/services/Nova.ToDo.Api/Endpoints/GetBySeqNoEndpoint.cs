using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Models;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// Pre-edit Get: <c>POST /api/v1/todos/by-seq-no</c>
/// Returns the full ToDo record for editing. Does NOT filter on frz_ind.
/// </summary>
public static class GetBySeqNoEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/by-seq-no", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoGetBySeqNo");
    }

    private static async Task<IResult> HandleAsync(
        GetBySeqNoRequest    request,
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

        if (request.SeqNo <= 0)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["seq_no"] = ["seq_no must be a positive integer."] },
                title: "Validation failed");

        // TODO: rights check — user must have read rights for ToDo

        string todo = dialect.TableRef("sales97", "ToDo");

        // Does not filter on frz_ind — returns the record regardless of frozen state.
        string sql = $"""
            SELECT
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
            WHERE SeqNo = @SeqNo
            """;

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        ToDoRow? row = await connection.QuerySingleOrDefaultAsync<ToDoRow>(
            sql, new { request.SeqNo }, commandTimeout: 30);

        if (row is null)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     $"ToDo record with seq_no {request.SeqNo} was not found.",
                statusCode: StatusCodes.Status404NotFound);

        return TypedResults.Ok(ToDoProjections.Project(row));
    }

    private sealed record GetBySeqNoRequest : RequestContext
    {
        public int SeqNo { get; init; }
    }
}
