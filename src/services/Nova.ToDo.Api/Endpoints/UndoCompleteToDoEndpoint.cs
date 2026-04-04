using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Configuration;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// Undo complete: <c>POST /api/v1/todos/{seq_no}/undo-complete</c>
/// Clears DoneInd, DoneBy, and DoneOn. Sets done_ind = false.
/// </summary>
public static class UndoCompleteToDoEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/{seqNo:int}/undo-complete", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoUndoComplete");
    }

    private static async Task<IResult> HandleAsync(
        int                                   seqNo,
        UndoCompleteRequest                   request,
        TenantContext                         tenantContext,
        IDbConnectionFactory                  connectionFactory,
        ISqlDialect                           dialect,
        IOptionsSnapshot<ConcurrencySettings> concurrencyOptions,
        CancellationToken                     ct)
    {
        Dictionary<string, string[]> contextErrors = RequestContextValidator.Validate(request);
        if (contextErrors.Count > 0)
            return TypedResults.ValidationProblem(contextErrors, title: "Validation failed");

        if (!RequestContextValidator.TenantMatches(request, tenantContext))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id in the request body does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        if (request.UpdatedOn == default)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["updated_on"] = ["updated_on is required for concurrency check."] },
                title: "Validation failed");

        // TODO: rights check — user must have complete rights for ToDo (same as complete)

        string todo   = dialect.TableRef("sales97", "ToDo");
        string doneOff = dialect.BooleanLiteral(false);

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        CurrentRow? current = await connection.QuerySingleOrDefaultAsync<CurrentRow>(
            $"SELECT SeqNo, DoneInd, UpdatedOn FROM {todo} WHERE SeqNo = @SeqNo",
            new { SeqNo = seqNo }, commandTimeout: 30);

        if (current is null)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     $"ToDo record with seq_no {seqNo} was not found.",
                statusCode: StatusCodes.Status404NotFound);

        ConcurrencySettings concurrency = concurrencyOptions.Value;
        if (concurrency.StrictMode)
        {
            DateTimeOffset dbOffset = new(DateTime.SpecifyKind(current.UpdatedOn, DateTimeKind.Utc));
            if (dbOffset > request.UpdatedOn)
                return TypedResults.Problem(
                    title:      "Conflict",
                    detail:     concurrency.ConflictMessage,
                    statusCode: StatusCodes.Status409Conflict);
        }

        // TODO (item 3): GETUTCDATE() is MSSQL-only — replace per dialect.
        await connection.ExecuteAsync(
            $"""
            UPDATE {todo} SET
                DoneInd   = {doneOff},
                DoneBy    = NULL,
                DoneOn    = NULL,
                UpdatedBy = @UpdatedBy,
                UpdatedOn = GETUTCDATE(),
                UpdatedAt = @UpdatedAt
            WHERE SeqNo = @SeqNo
            """,
            new
            {
                UpdatedBy = request.UserId,
                UpdatedAt = request.IpAddress ?? "unknown",
                SeqNo     = seqNo,
            },
            commandTimeout: 30);

        return TypedResults.Ok(new { seq_no = seqNo.ToString(), done_ind = false });
    }

    private sealed record CurrentRow(int SeqNo, bool DoneInd, DateTime UpdatedOn);

    private sealed record UndoCompleteRequest : RequestContext
    {
        public DateTimeOffset UpdatedOn { get; init; }
    }
}
