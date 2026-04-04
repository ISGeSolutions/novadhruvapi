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
/// Freeze / Unfreeze: <c>POST /api/v1/todos/{seq_no}/freeze</c>
/// frz_ind = true → freeze (soft-delete). frz_ind = false → unfreeze.
/// Freezing an already-frozen record returns 422.
/// </summary>
public static class FreezeToDoEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/{seqNo:int}/freeze", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoFreeze");
    }

    private static async Task<IResult> HandleAsync(
        int                                   seqNo,
        FreezeToDoRequest                     request,
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

        // TODO: rights check — user must have freeze rights for ToDo

        string todo = dialect.TableRef("sales97", "ToDo");

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        CurrentRow? current = await connection.QuerySingleOrDefaultAsync<CurrentRow>(
            $"SELECT SeqNo, FrzInd, UpdatedOn FROM {todo} WHERE SeqNo = @SeqNo",
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

        if (request.FrzInd && current.FrzInd)
            return TypedResults.Problem(
                title:      "Unprocessable Entity",
                detail:     "Record is already frozen.",
                statusCode: StatusCodes.Status422UnprocessableEntity);

        // TODO (item 3): GETUTCDATE() is MSSQL-only — replace per dialect.
        await connection.ExecuteAsync(
            $"""
            UPDATE {todo} SET
                FrzInd    = @FrzInd,
                UpdatedBy = @UpdatedBy,
                UpdatedOn = GETUTCDATE(),
                UpdatedAt = @UpdatedAt
            WHERE SeqNo = @SeqNo
            """,
            new
            {
                FrzInd    = request.FrzInd,
                UpdatedBy = request.UserId,
                UpdatedAt = request.IpAddress ?? "unknown",
                SeqNo     = seqNo,
            },
            commandTimeout: 30);

        return TypedResults.Ok(new { seq_no = seqNo.ToString(), frz_ind = request.FrzInd });
    }

    private sealed record CurrentRow(int SeqNo, bool FrzInd, DateTime UpdatedOn);

    private sealed record FreezeToDoRequest : RequestContext
    {
        public bool           FrzInd    { get; init; }
        public DateTimeOffset UpdatedOn { get; init; }
    }
}
