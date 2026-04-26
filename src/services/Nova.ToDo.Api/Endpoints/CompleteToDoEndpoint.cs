using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Models;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// Complete: <c>POST /api/v1/todos/{seq_no}/complete</c>
/// Marks a task as done. done_on is server-set to UTC now.
/// Returns 422 if the task is already completed.
/// </summary>
public static class CompleteToDoEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/{seqNo:int}/complete", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoComplete");
    }

    private static async Task<IResult> HandleAsync(
        int                  seqNo,
        CompleteToDoRequest  request,
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
        if (string.IsNullOrWhiteSpace(request.DoneBy))
            domainErrors["done_by"] = ["done_by is required."];
        if (request.ExpectedLockVer is null)
            domainErrors["expected_lock_ver"] = ["expected_lock_ver is required for concurrency check."];
        if (domainErrors.Count > 0)
            return TypedResults.ValidationProblem(domainErrors, title: "Validation failed");

        // TODO: rights check — user must have complete rights for ToDo

        string todo    = dialect.TableRef("sales97", "ToDo");
        int    nextVer = ConcurrencyHelper.NextVersion(request.ExpectedLockVer!.Value);

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        // MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by rajeevjha on 14 Apr 2026.
        CurrentRow? current = await connection.QuerySingleOrDefaultAsync<CurrentRow>(
            $"SELECT SeqNo, DoneInd FROM {todo} WHERE SeqNo = @SeqNo",
            new { SeqNo = seqNo }, commandTimeout: 30);

        if (current is null)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     $"ToDo record with seq_no {seqNo} was not found.",
                statusCode: StatusCodes.Status404NotFound);

        if (current.DoneInd)
            return TypedResults.Problem(
                title:      "Unprocessable Entity",
                detail:     "Task is already completed.",
                statusCode: StatusCodes.Status422UnprocessableEntity);

        string doneOn = dialect.BooleanLiteral(true);

        // MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by rajeevjha on 14 Apr 2026.
        // TODO (item 3): GETUTCDATE() is MSSQL-only — replace per dialect when porting.
        // Record exists (confirmed by SELECT above); 0 rows means concurrent update — return 409.
        int affected = await connection.ExecuteAsync(
            $"""
            UPDATE {todo} SET
                DoneInd   = {doneOn},
                DoneBy    = @DoneBy,
                DoneOn    = GETUTCDATE(),
                UpdatedBy = @UpdatedBy,
                UpdatedOn = GETUTCDATE(),
                UpdatedAt = @UpdatedAt,
                lock_ver  = @NextLockVer
            WHERE SeqNo = @SeqNo AND lock_ver = @ExpectedLockVer
            """,
            new
            {
                DoneBy          = request.DoneBy,
                UpdatedBy       = request.UserId,
                UpdatedAt       = request.IpAddress ?? "unknown",
                SeqNo           = seqNo,
                ExpectedLockVer = request.ExpectedLockVer.Value,
                NextLockVer     = nextVer,
            },
            commandTimeout: 30);

        if (affected == 0)
        {
            ToDoRow? serverRow = await ToDoDbHelper.FetchBySeqNoAsync(connection, dialect, seqNo);
            return TypedResults.Problem(
                title:      "Conflict",
                detail:     "Record was updated by someone else. Refresh and try again.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["seq_no"]          = seqNo.ToString(),
                    ["server_lock_ver"] = serverRow?.LockVer,
                    ["server_row"]      = serverRow is not null ? ToDoProjections.Project(serverRow) : null,
                });
        }

        return TypedResults.Ok(new { seq_no = seqNo.ToString(), done_ind = true, lock_ver = nextVer });
    }

    private sealed record CurrentRow(int SeqNo, bool DoneInd);

    private sealed record CompleteToDoRequest : RequestContext
    {
        public string DoneBy           { get; init; } = string.Empty;
        public int?   ExpectedLockVer  { get; init; }
    }
}
