using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Models;

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
        int                  seqNo,
        UndoCompleteRequest  request,
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

        if (request.ExpectedLockVer is null)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["expected_lock_ver"] = ["expected_lock_ver is required for concurrency check."] },
                title: "Validation failed");

        // TODO: rights check — user must have complete rights for ToDo (same as complete)

        string todo    = dialect.TableRef("sales97", "ToDo");
        string doneOff = dialect.BooleanLiteral(false);
        int    nextVer = ConcurrencyHelper.NextVersion(request.ExpectedLockVer.Value);

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        // MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by rajeevjha on 14 Apr 2026.
        // TODO (item 3): GETUTCDATE() is MSSQL-only — replace per dialect when porting.
        int affected = await connection.ExecuteAsync(
            $"""
            UPDATE {todo} SET
                DoneInd   = {doneOff},
                DoneBy    = NULL,
                DoneOn    = NULL,
                UpdatedBy = @UpdatedBy,
                UpdatedOn = GETUTCDATE(),
                UpdatedAt = @UpdatedAt,
                lock_ver  = @NextLockVer
            WHERE SeqNo = @SeqNo AND lock_ver = @ExpectedLockVer
            """,
            new
            {
                UpdatedBy       = request.UserId,
                UpdatedAt       = request.IpAddress ?? "unknown",
                SeqNo           = seqNo,
                ExpectedLockVer = request.ExpectedLockVer.Value,
                NextLockVer     = nextVer,
            },
            commandTimeout: 30);

        if (affected == 0)
        {
            ToDoRow? current = await ToDoDbHelper.FetchBySeqNoAsync(connection, dialect, seqNo);
            if (current is null)
                return TypedResults.Problem(
                    title:      "Not found",
                    detail:     $"ToDo record with seq_no {seqNo} was not found.",
                    statusCode: StatusCodes.Status404NotFound);
            return TypedResults.Problem(
                title:      "Conflict",
                detail:     "Record was updated by someone else. Refresh and try again.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["seq_no"]          = seqNo.ToString(),
                    ["server_lock_ver"] = current.LockVer,
                    ["server_row"]      = ToDoProjections.Project(current),
                });
        }

        return TypedResults.Ok(new { seq_no = seqNo.ToString(), done_ind = false, lock_ver = nextVer });
    }

    private sealed record UndoCompleteRequest : RequestContext
    {
        public int? ExpectedLockVer { get; init; }
    }
}
