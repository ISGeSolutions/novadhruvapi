using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Models;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// Hard delete: <c>POST /api/v1/todos/{seq_no}/delete</c>
/// Permanently removes the record. Requires updated_on for concurrency.
/// Note: prefer freeze (soft-delete) over hard delete for audit purposes.
/// </summary>
public static class DeleteToDoEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos/{seqNo:int}/delete", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoDelete");
    }

    private static async Task<IResult> HandleAsync(
        int                  seqNo,
        DeleteToDoRequest    request,
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

        // TODO: rights check — user must have delete rights for ToDo

        string todo = dialect.TableRef("sales97", "ToDo");

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        // MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by rajeevjha on 14 Apr 2026.
        int affected = await connection.ExecuteAsync(
            $"DELETE FROM {todo} WHERE SeqNo = @SeqNo AND lock_ver = @ExpectedLockVer",
            new { SeqNo = seqNo, ExpectedLockVer = request.ExpectedLockVer.Value },
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

        return TypedResults.Ok(new { seq_no = seqNo.ToString(), deleted = true });
    }

    private sealed record DeleteToDoRequest : RequestContext
    {
        public int? ExpectedLockVer { get; init; }
    }
}
