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
    // Single update — Pattern A optimistic concurrency via lock_ver
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

        if (request.ExpectedLockVer is null)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["expected_lock_ver"] = ["Required for concurrency check."] },
                title: "Validation failed");

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
                DepartureId     = departure_id,
                GroupTaskId     = group_task_id,
                Status          = request.Status,
                Notes           = request.Notes,
                CompletedDate   = request.CompletedDate,
                Now             = OpsGroupsDbHelper.UtcNow(),
                UpdatedBy       = request.UserId,
                ExpectedLockVer = request.ExpectedLockVer.Value,
            },
            commandTimeout: 10);

        if (affected == 0)
        {
            // Distinguish 404 (row gone) from 409 (lock mismatch)
            GroupTaskRow? current = await conn.QueryFirstOrDefaultAsync<GroupTaskRow>(
                OpsGroupsDbHelper.GroupTaskByIdSql(db),
                new { request.TenantId, DepartureId = departure_id, GroupTaskId = group_task_id },
                commandTimeout: 10);

            if (current is null)
                return TypedResults.Problem(
                    title:      "Not found",
                    detail:     $"Group task '{group_task_id}' not found for departure '{departure_id}'.",
                    statusCode: StatusCodes.Status404NotFound);

            return TypedResults.Problem(
                title:      "Conflict",
                detail:     "This task was modified by another user. Refresh and re-apply your changes.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["group_task_id"]   = current.GroupTaskId,
                    ["server_lock_ver"] = current.LockVer,
                    ["server_row"]      = ServerRow(current),
                });
        }

        GroupTaskRow? updated = await conn.QueryFirstOrDefaultAsync<GroupTaskRow>(
            OpsGroupsDbHelper.GroupTaskByIdSql(db),
            new { request.TenantId, DepartureId = departure_id, GroupTaskId = group_task_id },
            commandTimeout: 10);

        return TypedResults.Ok(new
        {
            success        = true,
            group_task_id  = updated?.GroupTaskId,
            status         = updated?.Status,
            notes          = updated?.Notes,
            completed_date = updated?.CompletedDate,
            lock_ver       = updated?.LockVer,
        });
    }

    // -------------------------------------------------------------------------
    // Bulk update — Pattern A batch: partial success, 200 with { saved, conflicts }
    // Each row is independently updated; no wrapping transaction.
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
            return TypedResults.Ok(new { saved = Array.Empty<object>(), conflicts = Array.Empty<object>() });

        OpsGroupsDbSettings db  = opsGroupsDbOptions.Value;
        DateTimeOffset      now = OpsGroupsDbHelper.UtcNow();

        var saved     = new List<object>();
        var conflicts = new List<object>();

        using IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType);

        foreach (BulkUpdateItem item in request.Updates)
        {
            int affected = await conn.ExecuteAsync(
                OpsGroupsDbHelper.GroupTaskConditionalUpdateSql(db),
                new
                {
                    request.TenantId,
                    item.DepartureId,
                    item.GroupTaskId,
                    ExpectedLockVer = item.ExpectedLockVer,
                    Status          = item.NewStatus,
                    Notes           = item.Notes,
                    CompletedDate   = (DateOnly?)null,
                    Now             = now,
                    UpdatedBy       = request.UserId,
                },
                commandTimeout: 10);

            if (affected == 0)
            {
                GroupTaskRow? current = await conn.QueryFirstOrDefaultAsync<GroupTaskRow>(
                    OpsGroupsDbHelper.GroupTaskByIdSql(db),
                    new { request.TenantId, item.DepartureId, GroupTaskId = item.GroupTaskId },
                    commandTimeout: 10);

                if (current is null)
                    conflicts.Add(new { group_task_id = item.GroupTaskId, error = "not_found" });
                else
                    conflicts.Add(new
                    {
                        group_task_id   = item.GroupTaskId,
                        server_lock_ver = current.LockVer,
                        server_row      = ServerRow(current),
                    });
            }
            else
            {
                saved.Add(new
                {
                    group_task_id = item.GroupTaskId,
                    lock_ver      = item.ExpectedLockVer + 1,
                });
            }
        }

        return TypedResults.Ok(new { saved, conflicts });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static object ServerRow(GroupTaskRow r) => new
    {
        group_task_id  = r.GroupTaskId,
        departure_id   = r.DepartureId,
        template_code  = r.TemplateCode,
        status         = r.Status,
        due_date       = r.DueDate,
        completed_date = r.CompletedDate,
        notes          = r.Notes,
        source         = r.Source,
        lock_ver       = r.LockVer,
        updated_on     = r.UpdatedOn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
    };

    // -------------------------------------------------------------------------
    // DTOs
    // -------------------------------------------------------------------------

    private sealed record SingleUpdateRequest : RequestContext
    {
        public string?   Status          { get; init; }
        public string?   Notes           { get; init; }
        public DateOnly? CompletedDate   { get; init; }
        public int?      ExpectedLockVer { get; init; }
    }

    private sealed record BulkUpdateRequest : RequestContext
    {
        public List<BulkUpdateItem>? Updates { get; init; }
    }

    private sealed record BulkUpdateItem(
        string  DepartureId,
        string  GroupTaskId,
        int     ExpectedLockVer,
        string  NewStatus,
        string? Notes);
}
