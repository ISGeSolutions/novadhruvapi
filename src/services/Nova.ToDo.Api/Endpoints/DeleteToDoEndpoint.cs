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
        int                                   seqNo,
        DeleteToDoRequest                     request,
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

        // TODO: rights check — user must have delete rights for ToDo

        string todo = dialect.TableRef("sales97", "ToDo");

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        // MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by rajeevjha on 14 Apr 2026.
        DateTime? dbUpdatedOn = await connection.ExecuteScalarAsync<DateTime?>(
            $"SELECT UpdatedOn FROM {todo} WHERE SeqNo = @SeqNo",
            new { SeqNo = seqNo }, commandTimeout: 30);

        if (dbUpdatedOn is null)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     $"ToDo record with seq_no {seqNo} was not found.",
                statusCode: StatusCodes.Status404NotFound);

        ConcurrencySettings concurrency = concurrencyOptions.Value;
        if (concurrency.StrictMode)
        {
            DateTimeOffset dbOffset = new(DateTime.SpecifyKind(dbUpdatedOn.Value, DateTimeKind.Utc));
            if (dbOffset > request.UpdatedOn)
                return TypedResults.Problem(
                    title:      "Conflict",
                    detail:     concurrency.ConflictMessage,
                    statusCode: StatusCodes.Status409Conflict);
        }

        await connection.ExecuteAsync(
            $"DELETE FROM {todo} WHERE SeqNo = @SeqNo",
            new { SeqNo = seqNo }, commandTimeout: 30);

        return TypedResults.Ok(new { seq_no = seqNo.ToString(), deleted = true });
    }

    private sealed record DeleteToDoRequest : RequestContext
    {
        public DateTimeOffset UpdatedOn { get; init; }
    }
}
