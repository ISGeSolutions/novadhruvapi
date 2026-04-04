using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Models;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// Pre-edit Get: <c>POST /api/v1/todos/by-tourseries-departure</c>
/// Returns the first open ToDo for a given tour series + departure date + job code.
/// Does NOT filter on frz_ind. Filters on done_ind = false.
/// Note: tour_series_code maps to DB column Brochure_Code_Short.
/// </summary>
public static class GetByTourSeriesDepartureEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/by-tourseries-departure", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoGetByTourSeriesDeparture");
    }

    private static async Task<IResult> HandleAsync(
        GetByTourSeriesDepartureRequest request,
        TenantContext                   tenantContext,
        IDbConnectionFactory            connectionFactory,
        ISqlDialect                     dialect,
        CancellationToken               ct)
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
        if (string.IsNullOrWhiteSpace(request.TourSeriesCode))
            domainErrors["tour_series_code"] = ["tour_series_code is required."];
        if (request.DepDate == default)
            domainErrors["dep_date"] = ["dep_date is required."];
        if (string.IsNullOrWhiteSpace(request.JobCode))
            domainErrors["job_code"] = ["job_code is required."];
        if (domainErrors.Count > 0)
            return TypedResults.ValidationProblem(domainErrors, title: "Validation failed");

        // TODO: rights check — user must have read rights for ToDo

        string todo    = dialect.TableRef("sales97", "ToDo");
        string doneOff = dialect.BooleanLiteral(false);

        // tour_series_code maps to Brochure_Code_Short column.
        // TODO (item 2): SELECT TOP 1 is MSSQL-only — replace with LIMIT 1 for Postgres/MariaDB.
        // TODO (item 5): CONVERT(date, DepDate) is MSSQL-only — use CAST(DepDate AS DATE) for Postgres/MariaDB.
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
            WHERE Brochure_Code_Short = @TourSeriesCode
              AND CONVERT(date, DepDate) = @DepDate
              AND JobCode               = @JobCode
              AND DoneInd               = {doneOff}
            ORDER BY SeqNo DESC
            """;

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        ToDoRow? row = await connection.QuerySingleOrDefaultAsync<ToDoRow>(
            sql,
            new { request.TourSeriesCode, DepDate = request.DepDate.ToDateTime(TimeOnly.MinValue), request.JobCode },
            commandTimeout: 30);

        if (row is null)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     $"No open ToDo found for tour_series_code '{request.TourSeriesCode}', dep_date {request.DepDate}, job_code '{request.JobCode}'.",
                statusCode: StatusCodes.Status404NotFound);

        return TypedResults.Ok(ToDoProjections.Project(row));
    }

    private sealed record GetByTourSeriesDepartureRequest : RequestContext
    {
        public string   TourSeriesCode { get; init; } = string.Empty;
        public DateOnly DepDate        { get; init; }
        public string   JobCode        { get; init; } = string.Empty;
    }
}
