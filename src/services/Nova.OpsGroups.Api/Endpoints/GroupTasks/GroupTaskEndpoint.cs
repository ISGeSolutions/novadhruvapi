using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.OpsGroups.Api.Configuration;
using Nova.OpsGroups.Api.Endpoints;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.OpsGroups.Api.Endpoints.GroupTasks;

public static class GroupTaskEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/grouptour-task-departures/{departure_id}/group-tasks/{group_task_id}", HandleSingleUpdateAsync)
             .RequireAuthorization()
             .WithName("GroupTaskUpdate");

        group.MapPatch("/group-task-bulk-update-group-tasks", HandleBulkUpdateAsync)
             .RequireAuthorization()
             .WithName("GroupTaskBulkUpdate");
    }

    // -------------------------------------------------------------------------
    // Single update
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleSingleUpdateAsync(
        string                           departure_id,
        string                           group_task_id,
        SingleUpdateRequest              request,
        HttpContext                      httpContext,
        IDbConnectionFactory             connectionFactory,
        IOptions<OpsGroupsDbSettings>    opsGroupsDbOptions,
        CancellationToken                ct)
    {
        Dictionary<string, string[]> errors = RequestContextValidator.Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        string? jwtTenantId = httpContext.User.FindFirstValue("tenant_id");
        if (!string.Equals(request.TenantId, jwtTenantId, StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        OpsGroupsDbSettings db = opsGroupsDbOptions.Value;

        using IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType);

        int affected = await conn.ExecuteAsync(
            OpsGroupsDbHelper.GroupTaskUpdateSql(db),
            new
            {
                request.TenantId,
                DepartureId    = departure_id,
                GroupTaskId    = group_task_id,
                Status         = request.Status,
                Notes          = request.Notes,
                CompletedDate  = request.CompletedDate,
                Now            = OpsGroupsDbHelper.UtcNow(),
                UpdatedBy      = request.UserId,
            },
            commandTimeout: 10);

        if (affected == 0)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     $"Group task '{group_task_id}' not found for departure '{departure_id}'.",
                statusCode: StatusCodes.Status404NotFound);

        GroupTaskRow? updated = await conn.QueryFirstOrDefaultAsync<GroupTaskRow>(
            OpsGroupsDbHelper.GroupTaskByIdSql(db),
            new { request.TenantId, DepartureId = departure_id, GroupTaskId = group_task_id },
            commandTimeout: 10);

        return TypedResults.Ok(new
        {
            success       = true,
            group_task_id = updated?.GroupTaskId,
            status        = updated?.Status,
            notes         = updated?.Notes,
            completed_date = updated?.CompletedDate,
        });
    }

    // -------------------------------------------------------------------------
    // Bulk update
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleBulkUpdateAsync(
        BulkUpdateRequest                request,
        HttpContext                      httpContext,
        IDbConnectionFactory             connectionFactory,
        IOptions<OpsGroupsDbSettings>    opsGroupsDbOptions,
        CancellationToken                ct)
    {
        Dictionary<string, string[]> errors = RequestContextValidator.Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        string? jwtTenantId = httpContext.User.FindFirstValue("tenant_id");
        if (!string.Equals(request.TenantId, jwtTenantId, StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        if (request.Updates is null || request.Updates.Count == 0)
            return TypedResults.Ok(new { success = true, updated_count = 0, results = Array.Empty<object>() });

        OpsGroupsDbSettings db  = opsGroupsDbOptions.Value;
        DateTimeOffset      now = OpsGroupsDbHelper.UtcNow();

        using IDbConnection  conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType);
        conn.Open();
        using IDbTransaction tx   = conn.BeginTransaction();

        var results      = new List<object>();
        int updatedCount = 0;
        bool anyConflict = false;

        foreach (BulkUpdateItem item in request.Updates)
        {
            // Optimistic check
            var current = await conn.QueryFirstOrDefaultAsync<CurrentStatusRow>(
                OpsGroupsDbHelper.GroupTaskCurrentStatusSql(db),
                new { request.TenantId, item.DepartureId, item.GroupTaskId },
                tx,
                commandTimeout: 10);

            if (current is null)
            {
                results.Add(new { group_task_id = item.GroupTaskId, success = false, new_status = (string?)null, error = "not_found" });
                continue;
            }

            if (!string.Equals(current.Status, item.OldStatus, StringComparison.OrdinalIgnoreCase))
            {
                anyConflict = true;
                results.Add(new { group_task_id = item.GroupTaskId, success = false, new_status = current.Status, error = "optimistic_conflict" });
                continue;
            }

            await conn.ExecuteAsync(
                OpsGroupsDbHelper.GroupTaskUpdateSql(db),
                new
                {
                    request.TenantId,
                    item.DepartureId,
                    item.GroupTaskId,
                    Status        = item.NewStatus,
                    Notes         = item.Notes,
                    CompletedDate = (DateOnly?)null,
                    Now           = now,
                    UpdatedBy     = request.UserId,
                },
                tx,
                commandTimeout: 10);

            updatedCount++;
            results.Add(new { group_task_id = item.GroupTaskId, success = true, new_status = item.NewStatus });
        }

        if (anyConflict)
        {
            tx.Rollback();
            return TypedResults.Problem(
                title:      "Conflict",
                detail:     "One or more tasks had a status conflict. No changes were saved.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["results"] = results });
        }

        tx.Commit();
        return TypedResults.Ok(new
        {
            success       = true,
            updated_count = updatedCount,
            results,
        });
    }

    // -------------------------------------------------------------------------
    // DTOs
    // -------------------------------------------------------------------------
    private sealed record SingleUpdateRequest : RequestContext
    {
        public string?   Status        { get; init; }
        public string?   Notes         { get; init; }
        public DateOnly? CompletedDate { get; init; }
    }

    private sealed record BulkUpdateRequest : RequestContext
    {
        public List<BulkUpdateItem>? Updates { get; init; }
    }

    private sealed record BulkUpdateItem(
        string  DepartureId,
        string  GroupTaskId,
        string  OldStatus,
        string  NewStatus,
        string? Notes);

    private sealed record CurrentStatusRow(string DepartureId, string GroupTaskId, string Status);
}
