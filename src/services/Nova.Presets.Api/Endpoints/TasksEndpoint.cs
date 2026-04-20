using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.Presets.Api.Configuration;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;

namespace Nova.Presets.Api.Endpoints;

/// <summary>
/// Group task templates:
///   <c>POST  /api/v1/tasks</c>          — list
///   <c>PATCH /api/v1/tasks/{code}</c>   — save (partial update / soft-delete)
///   <c>PATCH /api/v1/tasks/reorder</c>  — drag-drop reorder (atomic)
///
/// Relocated from Nova.OpsGroups.Api. Renamed from "activity templates" to "tasks".
/// frz_ind = soft-delete; no hard-delete endpoint exists.
/// sort_order: manual order; NULL rows sort last.
/// </summary>
public static class TasksEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        // Reorder must be registered before {code} so the literal "reorder" segment
        // is matched before the parameterised capture.
        group.MapPost("/tasks", HandleListAsync)
             .RequireAuthorization()
             .WithName("TasksList");

        group.MapPatch("/tasks/reorder", HandleReorderAsync)
             .RequireAuthorization()
             .WithName("TasksReorder");

        group.MapPatch("/tasks/{code}", HandleSaveAsync)
             .RequireAuthorization()
             .WithName("TasksSave");
    }

    // -------------------------------------------------------------------------
    // List
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleListAsync(
        ListRequest                  request,
        HttpContext                  httpContext,
        IDbConnectionFactory         connectionFactory,
        IOptions<PresetsDbSettings>  presetsDbOptions,
        CancellationToken            ct)
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

        PresetsDbSettings presetsDb = presetsDbOptions.Value;
        ISqlDialect       dialect   = PresetsDbHelper.Dialect(presetsDb.DbType);
        string            table     = dialect.TableRef("presets", "group_task_templates");
        string            falsy     = dialect.BooleanLiteral(false);
        string            truthy    = dialect.BooleanLiteral(true);

        bool includeFrozen = request.IncludeFrozen ?? false;
        string frzFilter   = includeFrozen ? string.Empty : $"AND frz_ind = {falsy}";

        IEnumerable<TaskRow> rows;
        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(
                   presetsDb.ConnectionString, presetsDb.DbType))
        {
            rows = await conn.QueryAsync<TaskRow>(
                $"""
                SELECT code                         AS Code,
                       name                         AS Name,
                       required                     AS Required,
                       critical                     AS Critical,
                       group_task_sla_offset_days   AS GroupTaskSlaOffsetDays,
                       reference_date               AS ReferenceDate,
                       source                       AS Source,
                       sort_order                   AS SortOrder,
                       frz_ind                      AS FrzInd
                FROM   {table}
                WHERE  tenant_id = @TenantId
                {frzFilter}
                ORDER  BY COALESCE(sort_order, 2147483647), code
                """,
                new { request.TenantId },
                commandTimeout: 10);
        }

        return TypedResults.Ok(new
        {
            tasks = rows.Select(r => new
            {
                code                       = r.Code,
                name                       = r.Name,
                required                   = r.Required,
                critical                   = r.Critical,
                group_task_sla_offset_days = r.GroupTaskSlaOffsetDays,
                reference_date             = r.ReferenceDate,
                source                     = r.Source,
                sort_order                 = r.SortOrder,
                frz_ind                    = r.FrzInd,
            }).ToList(),
        });
    }

    // -------------------------------------------------------------------------
    // Save (partial update)
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleSaveAsync(
        string                       code,
        SaveRequest                  request,
        HttpContext                  httpContext,
        IDbConnectionFactory         connectionFactory,
        IOptions<PresetsDbSettings>  presetsDbOptions,
        CancellationToken            ct)
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

        PresetsDbSettings presetsDb = presetsDbOptions.Value;

        var setClauses = new List<string>();
        var p          = new DynamicParameters();
        p.Add("TenantId", request.TenantId);
        p.Add("Code",     code);
        p.Add("Now",      PresetsDbHelper.UtcNow());
        p.Add("UpdatedBy", request.UserId);

        if (request.Name          is not null) { setClauses.Add("name = @Name");                           p.Add("Name",          request.Name); }
        if (request.Required      is not null) { setClauses.Add("required = @Required");                   p.Add("Required",      request.Required); }
        if (request.Critical      is not null) { setClauses.Add("critical = @Critical");                   p.Add("Critical",      request.Critical); }
        if (request.GroupTaskSlaOffsetDays is not null) { setClauses.Add("group_task_sla_offset_days = @SlaOffset"); p.Add("SlaOffset", request.GroupTaskSlaOffsetDays); }
        if (request.ReferenceDate is not null) { setClauses.Add("reference_date = @ReferenceDate");        p.Add("ReferenceDate", request.ReferenceDate); }
        if (request.Source        is not null) { setClauses.Add("source = @Source");                       p.Add("Source",        request.Source); }
        if (request.FrzInd        is not null) { setClauses.Add("frz_ind = @FrzInd");                     p.Add("FrzInd",        request.FrzInd); }

        if (setClauses.Count == 0)
            return TypedResults.Ok(new { success = true });

        setClauses.Add("updated_on = @Now");
        setClauses.Add("updated_by = @UpdatedBy");
        setClauses.Add("updated_at = 'Nova.Presets.Api'");

        ISqlDialect dialect = PresetsDbHelper.Dialect(presetsDb.DbType);
        string      table   = dialect.TableRef("presets", "group_task_templates");

        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(
                   presetsDb.ConnectionString, presetsDb.DbType))
        {
            int affected = await conn.ExecuteAsync(
                $"""
                UPDATE {table}
                SET    {string.Join(", ", setClauses)}
                WHERE  tenant_id = @TenantId
                AND    code      = @Code
                """,
                p,
                commandTimeout: 10);

            if (affected == 0)
                return TypedResults.Problem(
                    title:      "Not found",
                    detail:     $"No task template with code '{code}' found for this tenant.",
                    statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(new { success = true });
    }

    // -------------------------------------------------------------------------
    // Reorder — atomic drag-drop sort_order update
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleReorderAsync(
        ReorderRequest               request,
        HttpContext                  httpContext,
        IDbConnectionFactory         connectionFactory,
        IOptions<PresetsDbSettings>  presetsDbOptions,
        CancellationToken            ct)
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

        if (request.Order is null || request.Order.Count == 0)
            return TypedResults.Ok(new { success = true });

        PresetsDbSettings presetsDb = presetsDbOptions.Value;
        ISqlDialect       dialect   = PresetsDbHelper.Dialect(presetsDb.DbType);
        string            table     = dialect.TableRef("presets", "group_task_templates");
        DateTimeOffset    now       = PresetsDbHelper.UtcNow();

        using IDbConnection conn = connectionFactory.CreateFromConnectionString(
            presetsDb.ConnectionString, presetsDb.DbType);

        conn.Open();
        using IDbTransaction tx = conn.BeginTransaction();

        // First verify all codes exist for this tenant (to produce 409 + unknown_codes on mismatch)
        var existingCodes = (await conn.QueryAsync<string>(
            $"""
            SELECT code FROM {table}
            WHERE  tenant_id = @TenantId
            AND    code IN @Codes
            """,
            new { request.TenantId, Codes = request.Order.Select(o => o.Code).ToList() },
            tx,
            commandTimeout: 10)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknownCodes = request.Order
            .Select(o => o.Code)
            .Where(c => !existingCodes.Contains(c))
            .ToList();

        if (unknownCodes.Count > 0)
        {
            tx.Rollback();
            return TypedResults.Problem(
                title:      "Conflict",
                detail:     "One or more task codes were not found for this tenant.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["unknown_codes"] = unknownCodes });
        }

        // Apply each sort_order update individually within the transaction
        foreach (OrderItem item in request.Order)
        {
            await conn.ExecuteAsync(
                $"""
                UPDATE {table}
                SET    sort_order = @SortOrder,
                       updated_on = @Now,
                       updated_by = @UpdatedBy,
                       updated_at = 'Nova.Presets.Api'
                WHERE  tenant_id  = @TenantId
                AND    code       = @Code
                """,
                new { item.SortOrder, Now = now, UpdatedBy = request.UserId, request.TenantId, item.Code },
                tx,
                commandTimeout: 10);
        }

        tx.Commit();
        return TypedResults.Ok(new { success = true });
    }

    // -------------------------------------------------------------------------
    // DTOs
    // -------------------------------------------------------------------------
    private sealed record TaskRow(
        string  Code,
        string  Name,
        bool    Required,
        bool    Critical,
        int?    GroupTaskSlaOffsetDays,
        string  ReferenceDate,
        string  Source,
        int?    SortOrder,
        bool    FrzInd);

    private sealed record ListRequest : RequestContext
    {
        public bool? IncludeFrozen { get; set; }
    }

    private sealed record SaveRequest : RequestContext
    {
        public string? Name                    { get; set; }
        public bool?   Required                { get; set; }
        public bool?   Critical                { get; set; }
        public int?    GroupTaskSlaOffsetDays  { get; set; }
        public string? ReferenceDate           { get; set; }
        public string? Source                  { get; set; }
        public bool?   FrzInd                  { get; set; }
    }

    private sealed record ReorderRequest : RequestContext
    {
        public List<OrderItem>? Order { get; set; }
    }

    private sealed record OrderItem(string Code, int SortOrder);
}
