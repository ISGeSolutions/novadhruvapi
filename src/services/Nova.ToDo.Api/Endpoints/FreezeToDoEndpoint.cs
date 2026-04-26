using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Models;

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
        int                  seqNo,
        FreezeToDoRequest    request,
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

        // TODO: rights check — user must have freeze rights for ToDo

        string todo   = dialect.TableRef("sales97", "ToDo");
        int    nextVer = ConcurrencyHelper.NextVersion(request.ExpectedLockVer.Value);

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        // MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by rajeevjha on 14 Apr 2026.
        CurrentRow? current = await connection.QuerySingleOrDefaultAsync<CurrentRow>(
            $"SELECT SeqNo, ISNULL(FrzInd, 0) AS frz_ind FROM {todo} WHERE SeqNo = @SeqNo",
            new { SeqNo = seqNo }, commandTimeout: 30);

        if (current is null)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     $"ToDo record with seq_no {seqNo} was not found.",
                statusCode: StatusCodes.Status404NotFound);

        if (request.FrzInd && current.FrzInd)
            return TypedResults.Problem(
                title:      "Unprocessable Entity",
                detail:     "Record is already frozen.",
                statusCode: StatusCodes.Status422UnprocessableEntity);

        // MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by rajeevjha on 14 Apr 2026.
        // TODO (item 3): GETUTCDATE() is MSSQL-only — replace per dialect when porting.
        // Record exists (confirmed by SELECT above); 0 rows means concurrent update — return 409.
        int affected = await connection.ExecuteAsync(
            $"""
            UPDATE {todo} SET
                FrzInd    = @FrzInd,
                UpdatedBy = @UpdatedBy,
                UpdatedOn = GETUTCDATE(),
                UpdatedAt = @UpdatedAt,
                lock_ver  = @NextLockVer
            WHERE SeqNo = @SeqNo AND lock_ver = @ExpectedLockVer
            """,
            new
            {
                FrzInd          = request.FrzInd,
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

        return TypedResults.Ok(new { seq_no = seqNo.ToString(), frz_ind = request.FrzInd, lock_ver = nextVer });
    }

    private sealed record CurrentRow(int SeqNo, bool FrzInd);

    private sealed record FreezeToDoRequest : RequestContext
    {
        public bool FrzInd          { get; init; }
        public int? ExpectedLockVer { get; init; }
    }
}
