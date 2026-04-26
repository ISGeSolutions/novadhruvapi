using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using Nova.OpsGroups.Api.Configuration;
using Nova.OpsGroups.Api.Endpoints;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Validation;
using NovaDbType = Nova.Shared.Data.DbType;

namespace Nova.OpsGroups.Api.Endpoints.SlaRules;

public static class SlaHierarchyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/group-task-sla-hierarchy",   HandleHierarchyAsync)
             .RequireAuthorization()
             .WithName("SlaHierarchy");

        group.MapPatch("/group-task-sla-rule-save",  HandleRuleSaveAsync)
             .RequireAuthorization()
             .WithName("SlaRuleSave");

        group.MapPost("/group-task-sla-audit",       HandleAuditAsync)
             .RequireAuthorization()
             .WithName("SlaAudit");

        group.MapPost("/group-task-codes-available", HandleCodesAvailableAsync)
             .RequireAuthorization()
             .WithName("SlaCodesAvailable");
    }

    // -------------------------------------------------------------------------
    // Full hierarchy fetch — GLOB + TG + all series + all departures in one call
    // Request:  { tenant_id, tour_generic_code, year_floor }
    // Response: { global: Level, levels: [Level...] }
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleHierarchyAsync(
        HierarchyRequest              request,
        HttpContext                   httpContext,
        IDbConnectionFactory          connectionFactory,
        IOptions<OpsGroupsDbSettings> opsGroupsDbOptions,
        CancellationToken             ct)
    {
        Dictionary<string, string[]> errors = RequestContextValidator.Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        if (string.IsNullOrWhiteSpace(request.TourGenericCode))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["tour_generic_code"] = ["Required."] },
                title: "Validation failed");

        string? jwtTenantId = httpContext.User.FindFirstValue("tenant_id");
        if (!string.Equals(request.TenantId, jwtTenantId, StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        OpsGroupsDbSettings db   = opsGroupsDbOptions.Value;
        using IDbConnection  conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType);

        bool globalOnly = string.Equals(request.TourGenericCode, "GLOBAL", StringComparison.OrdinalIgnoreCase);

        GlobalInfoRow?           globInfo;
        TourGenericHierarchyRow? tgInfo = null;

        if (globalOnly)
        {
            globInfo = await conn.QueryFirstOrDefaultAsync<GlobalInfoRow>(
                OpsGroupsDbHelper.FetchGlobalInfoSql(db.DbType),
                new { request.TenantId },
                commandTimeout: 10);
            if (globInfo is null)
                return TypedResults.Problem(title: "Not Found", detail: "Global scope not found.", statusCode: 404);
        }
        else
        {
            tgInfo = await conn.QueryFirstOrDefaultAsync<TourGenericHierarchyRow>(
                OpsGroupsDbHelper.FetchTourGenericWithGlobSql(db.DbType),
                new { request.TenantId, TourGenericCode = request.TourGenericCode },
                commandTimeout: 10);
            if (tgInfo is null)
                return TypedResults.Problem(title: "Not Found", detail: "Tour generic not found.", statusCode: 404);
            globInfo = new GlobalInfoRow(tgInfo.GlobId, "Global");
        }

        List<TourSeriesHierarchyRow>    seriesList    = [];
        List<TourDepartureHierarchyRow> departureList = [];

        if (!globalOnly && tgInfo is not null)
        {
            seriesList = (await conn.QueryAsync<TourSeriesHierarchyRow>(
                OpsGroupsDbHelper.FetchSeriesForTgSql(db.DbType),
                new { request.TenantId, TgId = tgInfo.TgId },
                commandTimeout: 10)).ToList();

            DateOnly yearFloorDate = new DateOnly(request.YearFloor ?? DateTimeOffset.UtcNow.Year, 1, 1);
            departureList = (await conn.QueryAsync<TourDepartureHierarchyRow>(
                OpsGroupsDbHelper.FetchDeparturesForTgSql(db.DbType),
                new { request.TenantId, TgId = tgInfo.TgId, YearFloorDate = yearFloorDate },
                commandTimeout: 10)).ToList();
        }

        var taskCodes = (await conn.QueryAsync<string>(
            OpsGroupsDbHelper.TaskTemplatesForTenantSql(db.DbType),
            new { request.TenantId },
            commandTimeout: 10)).ToList();

        Guid[] scopeIds  = CollectScopeIds(globInfo.GlobId, tgInfo, seriesList, departureList);
        var    allSlaCells = (await conn.QueryAsync<SlaTaskRow>(
            OpsGroupsDbHelper.SlaTaskFetchForScopesSql(db.DbType),
            new { request.TenantId, ScopeIds = scopeIds },
            commandTimeout: 10)).ToList();

        string[] refDates = ["departure", "return", "ji_exists"];

        var globalLevel = BuildLevel(
            id:            "global",
            type:          "global",
            label:         "GLOBAL",
            tourCode:      "GLOBAL",
            seriesName:    "Global",
            parentId:      null,
            departureDate: null,
            ownRows:       allSlaCells.Where(r => r.ScopeId == globInfo.GlobId).ToList(),
            chain:         [("GLOB", globInfo.GlobId)],
            allRows:       allSlaCells,
            taskCodes:     taskCodes,
            refDates:      refDates);

        var levels = new List<object>();

        if (!globalOnly && tgInfo is not null)
        {
            List<(string, Guid)> tgChain = [("TG", tgInfo.TgId), ("GLOB", globInfo.GlobId)];
            levels.Add(BuildLevel(
                id:            $"tg_{tgInfo.Code}",
                type:          "tour_generic",
                label:         $"{tgInfo.Code} — {tgInfo.Name}",
                tourCode:      tgInfo.Code,
                seriesName:    tgInfo.Name,
                parentId:      "global",
                departureDate: null,
                ownRows:       allSlaCells.Where(r => r.ScopeId == tgInfo.TgId).ToList(),
                chain:         tgChain,
                allRows:       allSlaCells,
                taskCodes:     taskCodes,
                refDates:      refDates));

            foreach (var series in seriesList)
            {
                List<(string, Guid)> tsChain = [("TS", series.Id), ("TG", tgInfo.TgId), ("GLOB", globInfo.GlobId)];
                levels.Add(BuildLevel(
                    id:            $"ts_{series.SeriesCode}",
                    type:          "tour_series",
                    label:         $"{series.SeriesCode} — {series.SeriesName}",
                    tourCode:      series.SeriesCode,
                    seriesName:    series.SeriesName,
                    parentId:      $"tg_{tgInfo.Code}",
                    departureDate: null,
                    ownRows:       allSlaCells.Where(r => r.ScopeId == series.Id).ToList(),
                    chain:         tsChain,
                    allRows:       allSlaCells,
                    taskCodes:     taskCodes,
                    refDates:      refDates));

                foreach (var dep in departureList.Where(d => d.TsId == series.Id).OrderBy(d => d.DepartureDate))
                {
                    List<(string, Guid)> tdChain = [("TD", dep.Id), ("TS", series.Id), ("TG", tgInfo.TgId), ("GLOB", globInfo.GlobId)];
                    levels.Add(BuildLevel(
                        id:            $"dep_{dep.Id}",
                        type:          "departure",
                        label:         dep.DepartureDate.ToString("dd MMM yyyy"),
                        tourCode:      series.SeriesCode,
                        seriesName:    series.SeriesName,
                        parentId:      $"ts_{series.SeriesCode}",
                        departureDate: dep.DepartureDate,
                        ownRows:       allSlaCells.Where(r => r.ScopeId == dep.Id).ToList(),
                        chain:         tdChain,
                        allRows:       allSlaCells,
                        taskCodes:     taskCodes,
                        refDates:      refDates));
                }
            }
        }

        return TypedResults.Ok(new { global = globalLevel, levels });
    }

    // -------------------------------------------------------------------------
    // Per-cell save — renamed fields (GroupTaskCode, ReferenceDate, Old, New)
    // Scope comes as nested { level, tour_generic_code, ... } object.
    // Returns { success, version, updated_at, updated_by }.
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleRuleSaveAsync(
        RuleSaveRequest               request,
        HttpContext                   httpContext,
        IDbConnectionFactory          connectionFactory,
        IOptions<OpsGroupsDbSettings> opsGroupsDbOptions,
        CancellationToken             ct)
    {
        Dictionary<string, string[]> errors = RequestContextValidator.Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        if (request.Scope is null)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["scope"] = ["Required."] },
                title: "Validation failed");

        string? internalType = ToInternalScopeType(request.Scope.Level);
        if (internalType is null)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["scope.level"] = ["Must be global, tour_generic, tour_series, or departure."] },
                title: "Validation failed");

        if (internalType == "GLOB" && (request.Changes?.Any(c => c.New.Kind == "inherit") ?? false))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["changes"] = ["'inherit' is not valid at the global scope."] },
                title: "Validation failed");

        if (request.Changes is null || request.Changes.Count == 0)
            return TypedResults.Ok(new { success = true, version = (string?)null, updated_at = (string?)null, updated_by = (string?)null });

        string? jwtTenantId = httpContext.User.FindFirstValue("tenant_id");
        if (!string.Equals(request.TenantId, jwtTenantId, StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        OpsGroupsDbSettings db  = opsGroupsDbOptions.Value;
        DateTimeOffset      now = OpsGroupsDbHelper.UtcNow();

        using IDbConnection  conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType);
        conn.Open();
        using IDbTransaction tx   = conn.BeginTransaction();

        var scopeResolved = await ResolveScopeIdAsync(conn, db.DbType, request.TenantId!, internalType,
            request.Scope.TourGenericCode, request.Scope.TourSeriesCode, request.Scope.DepartureId, tx);

        if (scopeResolved is null)
        {
            tx.Rollback();
            return TypedResults.Problem(title: "Not Found", detail: "Scope not found.", statusCode: 404);
        }

        Guid   requestedScopeId = scopeResolved.Value.ScopeId;
        string slaSql           = OpsGroupsDbHelper.Dialect(db.DbType).TableRef("presets", "sla_task");

        // Phase 1 — read current rows and detect conflicts before writing anything
        var conflicts   = new List<ConflictDetail>();
        var currentRows = new Dictionary<(string RefDate, string TaskCode), SlaTaskRow?>();

        foreach (RuleSaveChange change in request.Changes)
        {
            string dbRef = ToDbRefDate(change.ReferenceDate);

            SlaTaskRow? current = await conn.QueryFirstOrDefaultAsync<SlaTaskRow>(
                $"""
                SELECT kind, offset_days FROM {slaSql}
                WHERE  tenant_id = @TenantId AND scope_type = @ScopeType AND scope_id = @ScopeId
                AND    enq_event_code = @EnqEventCode AND task_code = @TaskCode
                """,
                new { request.TenantId, ScopeType = internalType, ScopeId = requestedScopeId,
                      EnqEventCode = dbRef, TaskCode = change.GroupTaskCode },
                tx,
                commandTimeout: 10);

            currentRows[(change.ReferenceDate, change.GroupTaskCode)] = current;

            CellDto dbCell = current is null       ? CellDto.Inherit
                           : current.Kind == "SET" ? CellDto.Set(current.OffsetDays!.Value)
                                                   : CellDto.Na;

            if (!CellsMatch(dbCell, change.Old))
                conflicts.Add(new ConflictDetail(change.ReferenceDate, change.GroupTaskCode, change.Old, dbCell));
        }

        if (conflicts.Count > 0)
        {
            tx.Rollback();
            return TypedResults.Problem(
                title:      "Conflict",
                detail:     $"{conflicts.Count} cell(s) were changed by another user since you opened the grid. Refresh and re-apply your changes.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["conflict_cells"] = conflicts });
        }

        // Phase 2 — no conflicts: apply all writes using cached current rows
        foreach (RuleSaveChange change in request.Changes)
        {
            string      dbRef   = ToDbRefDate(change.ReferenceDate);
            SlaTaskRow? current = currentRows[(change.ReferenceDate, change.GroupTaskCode)];

            string? kindOld   = current?.Kind;
            int?    offsetOld = current?.OffsetDays;
            string? kindNew   = change.New.Kind == "inherit" ? null : change.New.Kind.ToUpper();
            int?    offsetNew = change.New.Kind == "set"     ? change.New.OffsetDays : (int?)null;

            if (change.New.Kind == "inherit")
            {
                await conn.ExecuteAsync(
                    OpsGroupsDbHelper.SlaTaskDeleteSql(db.DbType),
                    new { request.TenantId, ScopeType = internalType, ScopeId = requestedScopeId,
                          EnqEventCode = dbRef, TaskCode = change.GroupTaskCode },
                    tx,
                    commandTimeout: 10);
            }
            else
            {
                await conn.ExecuteAsync(
                    OpsGroupsDbHelper.SlaTaskUpsertSql(db.DbType),
                    new
                    {
                        Id           = Guid.CreateVersion7(),
                        request.TenantId,
                        ScopeType    = internalType,
                        ScopeId      = requestedScopeId,
                        EnqEventCode = dbRef,
                        TaskCode     = change.GroupTaskCode,
                        Kind         = change.New.Kind.ToUpper(),
                        OffsetDays   = change.New.Kind == "set" ? change.New.OffsetDays : (int?)null,
                        UpdatedBy    = request.UserId,
                        Now          = now,
                    },
                    tx,
                    commandTimeout: 10);
            }

            await conn.ExecuteAsync(
                OpsGroupsDbHelper.SlaTaskAuditInsertSql(db.DbType),
                new
                {
                    Id            = Guid.CreateVersion7(),
                    request.TenantId,
                    ScopeType     = internalType,
                    ScopeId       = requestedScopeId,
                    EnqEventCode  = dbRef,
                    TaskCode      = change.GroupTaskCode,
                    KindOld       = kindOld,
                    OffsetDaysOld = offsetOld,
                    KindNew       = kindNew,
                    OffsetDaysNew = offsetNew,
                    ChangedBy     = request.UserId,
                    Now           = now,
                },
                tx,
                commandTimeout: 10);
        }

        tx.Commit();
        return TypedResults.Ok(new
        {
            success    = true,
            version    = now.UtcTicks.ToString("x"),
            updated_at = now.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            updated_by = request.UserId,
        });
    }

    // -------------------------------------------------------------------------
    // Audit fetch — accepts scope_key string (global / tg_X / ts_X / dep_<uuid>)
    // Response fields: group_task_code, reference_date, changed_at
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleAuditAsync(
        AuditRequest                  request,
        HttpContext                   httpContext,
        IDbConnectionFactory          connectionFactory,
        IOptions<OpsGroupsDbSettings> opsGroupsDbOptions,
        CancellationToken             ct)
    {
        Dictionary<string, string[]> errors = RequestContextValidator.Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, title: "Validation failed");

        if (string.IsNullOrWhiteSpace(request.ScopeKey))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["scope_key"] = ["Required."] },
                title: "Validation failed");

        string? jwtTenantId = httpContext.User.FindFirstValue("tenant_id");
        if (!string.Equals(request.TenantId, jwtTenantId, StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem(
                title:      "Forbidden",
                detail:     "tenant_id does not match the authenticated tenant.",
                statusCode: StatusCodes.Status403Forbidden);

        var parsed = ParseScopeKey(request.ScopeKey);
        if (parsed is null)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["scope_key"] = ["Invalid format. Use 'global', 'tg_<code>', 'ts_<code>', or 'dep_<uuid>'."] },
                title: "Validation failed");

        int pageSize = Math.Min(request.PageSize ?? 50, 200);
        int page     = Math.Max(request.Page    ?? 1,   1);
        int skip     = (page - 1) * pageSize;

        OpsGroupsDbSettings db   = opsGroupsDbOptions.Value;
        using IDbConnection  conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType);

        var scopeResolved = await ResolveScopeIdAsync(conn, db.DbType, request.TenantId!,
            parsed.InternalType, parsed.TourGenericCode, parsed.SeriesCode, parsed.DepartureId);

        if (scopeResolved is null)
            return TypedResults.Problem(title: "Not Found", detail: "Scope not found.", statusCode: 404);

        Guid scopeId = scopeResolved.Value.ScopeId;

        int total = await conn.ExecuteScalarAsync<int>(
            OpsGroupsDbHelper.SlaTaskAuditCountSql(db.DbType),
            new { request.TenantId, ScopeId = scopeId },
            commandTimeout: 10);

        var rows = (await conn.QueryAsync<SlaAuditRow>(
            OpsGroupsDbHelper.SlaTaskAuditFetchSql(db.DbType, skip, pageSize),
            new { request.TenantId, ScopeId = scopeId },
            commandTimeout: 10)).ToList();

        return TypedResults.Ok(new
        {
            scope_key = request.ScopeKey,
            page,
            page_size = pageSize,
            total,
            entries   = rows.Select(r => new
            {
                id              = r.Id,
                group_task_code = r.TaskCode,
                reference_date  = ToWireRefDate(r.EnqEventCode),
                old_cell        = r.KindOld is null ? null : MakeAuditCellObject(r.KindOld, r.OffsetDaysOld),
                new_cell        = r.KindNew is null ? null : MakeAuditCellObject(r.KindNew, r.OffsetDaysNew),
                changed_by      = r.ChangedBy,
                changed_at      = r.ChangedOn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            }).ToList(),
        });
    }

    // -------------------------------------------------------------------------
    // Task codes available — unchanged
    // -------------------------------------------------------------------------
    private static async Task<IResult> HandleCodesAvailableAsync(
        CodesAvailableRequest         request,
        HttpContext                   httpContext,
        IDbConnectionFactory          connectionFactory,
        IOptions<OpsGroupsDbSettings> opsGroupsDbOptions,
        CancellationToken             ct)
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

        IEnumerable<TaskTemplateRow> templates;
        using (IDbConnection conn = connectionFactory.CreateFromConnectionString(db.ConnectionString, db.DbType))
        {
            templates = await conn.QueryAsync<TaskTemplateRow>(
                OpsGroupsDbHelper.TaskTemplatesForTenantSql(db.DbType),
                new { request.TenantId },
                commandTimeout: 10);
        }

        return TypedResults.Ok(new
        {
            available_codes = templates.Select(t => new { code = t.Code, name = t.Name, source = t.Source }).ToList(),
        });
    }

    // -------------------------------------------------------------------------
    // Scope ID resolution — returns (internalType, scopeId) for write/audit
    // -------------------------------------------------------------------------

    private static async Task<(string InternalType, Guid ScopeId)?> ResolveScopeIdAsync(
        IDbConnection   conn,
        NovaDbType      dbType,
        string          tenantId,
        string          internalType,
        string?         tourGenericCode = null,
        string?         seriesCode      = null,
        string?         departureId     = null,
        IDbTransaction? tx              = null)
    {
        return internalType switch
        {
            "GLOB" => await ResolveGlobScopeId(conn, dbType, tenantId, tx),
            "TG"   => await ResolveTgScopeId(conn, dbType, tenantId, tourGenericCode, tx),
            "TS"   => await ResolveTsScopeId(conn, dbType, tenantId, seriesCode, tx),
            "TD"   => await ResolveTdScopeId(conn, dbType, tenantId, departureId, tx),
            _      => null,
        };
    }

    private static async Task<(string, Guid)?> ResolveGlobScopeId(
        IDbConnection conn, NovaDbType dbType, string tenantId, IDbTransaction? tx)
    {
        var row = await conn.QueryFirstOrDefaultAsync<ScopeResolutionGlob>(
            OpsGroupsDbHelper.ResolveGlobalScopeSql(dbType),
            new { TenantId = tenantId }, tx, commandTimeout: 10);
        return row is null ? null : ("GLOB", row.GlobId);
    }

    private static async Task<(string, Guid)?> ResolveTgScopeId(
        IDbConnection conn, NovaDbType dbType, string tenantId, string? code, IDbTransaction? tx)
    {
        var row = await conn.QueryFirstOrDefaultAsync<ScopeResolutionTg>(
            OpsGroupsDbHelper.ResolveTourGenericScopeSql(dbType),
            new { TenantId = tenantId, TourGenericCode = code }, tx, commandTimeout: 10);
        return row is null ? null : ("TG", row.TgId);
    }

    private static async Task<(string, Guid)?> ResolveTsScopeId(
        IDbConnection conn, NovaDbType dbType, string tenantId, string? seriesCode, IDbTransaction? tx)
    {
        var row = await conn.QueryFirstOrDefaultAsync<ScopeResolutionTs>(
            OpsGroupsDbHelper.ResolveTourSeriesScopeSql(dbType),
            new { TenantId = tenantId, SeriesCode = seriesCode }, tx, commandTimeout: 10);
        return row is null ? null : ("TS", row.TsId);
    }

    private static async Task<(string, Guid)?> ResolveTdScopeId(
        IDbConnection conn, NovaDbType dbType, string tenantId, string? departureId, IDbTransaction? tx)
    {
        var row = await conn.QueryFirstOrDefaultAsync<ScopeResolutionTd>(
            OpsGroupsDbHelper.ResolveTourDepartureScopeSql(dbType),
            new { TenantId = tenantId, DepartureId = departureId }, tx, commandTimeout: 10);
        return row is null ? null : ("TD", row.TdId);
    }

    // -------------------------------------------------------------------------
    // Level and entry builders
    // -------------------------------------------------------------------------

    private static object BuildLevel(
        string                       id,
        string                       type,
        string                       label,
        string                       tourCode,
        string                       seriesName,
        string?                      parentId,
        DateOnly?                    departureDate,
        List<SlaTaskRow>             ownRows,
        List<(string Type, Guid Id)> chain,
        List<SlaTaskRow>             allRows,
        List<string>                 taskCodes,
        string[]                     refDates)
    {
        string? version = ComputeVersion(ownRows);
        var     entries = refDates.Select(rd => BuildEntry(rd, ownRows, chain, allRows, taskCodes)).ToList();

        return new
        {
            id,
            type,
            label,
            tour_code      = tourCode,
            series_name    = seriesName,
            parent_id      = parentId,
            departure_date = departureDate?.ToString("yyyy-MM-dd"),
            version,
            entries,
        };
    }

    private static object BuildEntry(
        string                       refDate,
        List<SlaTaskRow>             ownRows,
        List<(string Type, Guid Id)> chain,
        List<SlaTaskRow>             allRows,
        List<string>                 taskCodes)
    {
        string dbRef = ToDbRefDate(refDate);

        var cells = taskCodes.ToDictionary(
            tc => tc,
            tc =>
            {
                SlaTaskRow? ownRow = ownRows.FirstOrDefault(r => r.EnqEventCode == dbRef && r.TaskCode == tc);
                object? own = ownRow is null ? null : MakeCellObject(ownRow);

                object? resolved     = null;
                string? resolvedFrom = null;

                // Walk outermost → innermost. A scope's own `na` is terminal: once
                // any ancestor sets na, no inner scope can override it with `set`.
                foreach ((string chainType, Guid chainId) in Enumerable.Reverse(chain))
                {
                    SlaTaskRow? r = allRows.FirstOrDefault(x => x.ScopeId == chainId && x.EnqEventCode == dbRef && x.TaskCode == tc);
                    if (r is null) continue;
                    resolved     = MakeCellObject(r);
                    resolvedFrom = ToWireLevel(chainType);
                    if (r.Kind == "NA") break;
                }

                return (object)new { own, resolved, resolved_from = resolvedFrom };
            });

        return new { ref_date = refDate, cells };
    }

    private static object MakeCellObject(SlaTaskRow row) =>
        row.Kind == "SET"
            ? (object)new { kind = "set", offset_days = row.OffsetDays }
            : new { kind = "na" };

    private static object MakeAuditCellObject(string kind, int? offsetDays) =>
        kind.ToUpper() == "SET"
            ? (object)new { kind = "set", offset_days = offsetDays }
            : new { kind = kind.ToLower() };

    private static string? ComputeVersion(List<SlaTaskRow> rows) =>
        rows.Count == 0 ? null : rows.Max(r => r.UpdatedOn).UtcTicks.ToString("x");

    private static Guid[] CollectScopeIds(
        Guid                           globId,
        TourGenericHierarchyRow?       tgInfo,
        List<TourSeriesHierarchyRow>   series,
        List<TourDepartureHierarchyRow> departures)
    {
        var ids = new List<Guid> { globId };
        if (tgInfo is not null) ids.Add(tgInfo.TgId);
        foreach (var s in series)     ids.Add(s.Id);
        foreach (var d in departures) ids.Add(d.Id);
        return ids.ToArray();
    }

    // -------------------------------------------------------------------------
    // Mapping helpers — wire format ↔ DB codes
    // -------------------------------------------------------------------------

    private static string? ToInternalScopeType(string? level) => level switch
    {
        "global"       => "GLOB",
        "tour_generic" => "TG",
        "tour_series"  => "TS",
        "departure"    => "TD",
        _              => null,
    };

    private static string? ToWireLevel(string internalType) => internalType switch
    {
        "GLOB" => "global",
        "TG"   => "tour_generic",
        "TS"   => "tour_series",
        "TD"   => "departure",
        _      => null,
    };

    private static string ToDbRefDate(string wireRef) => wireRef switch
    {
        "departure" => "DP",
        "return"    => "RT",
        "ji_exists" => "JI",
        _           => wireRef,
    };

    private static string ToWireRefDate(string dbRef) => dbRef switch
    {
        "DP" => "departure",
        "RT" => "return",
        "JI" => "ji_exists",
        _    => dbRef.ToLower(),
    };

    private static ParsedScopeKey? ParseScopeKey(string key)
    {
        if (string.Equals(key, "global", StringComparison.OrdinalIgnoreCase))
            return new ParsedScopeKey("GLOB", null, null, null);

        if (key.StartsWith("tg_", StringComparison.OrdinalIgnoreCase))
        {
            string code = key[3..];
            return string.IsNullOrWhiteSpace(code) ? null : new ParsedScopeKey("TG", code, null, null);
        }

        if (key.StartsWith("ts_", StringComparison.OrdinalIgnoreCase))
        {
            string code = key[3..];
            return string.IsNullOrWhiteSpace(code) ? null : new ParsedScopeKey("TS", null, code, null);
        }

        if (key.StartsWith("dep_", StringComparison.OrdinalIgnoreCase))
        {
            string depId = key[4..];
            return string.IsNullOrWhiteSpace(depId) ? null : new ParsedScopeKey("TD", null, null, depId);
        }

        return null;
    }

    private static bool CellsMatch(CellDto a, CellDto b) =>
        string.Equals(a.Kind, b.Kind, StringComparison.OrdinalIgnoreCase) &&
        a.OffsetDays == b.OffsetDays;

    // -------------------------------------------------------------------------
    // Request records and DTOs
    // -------------------------------------------------------------------------

    private sealed record HierarchyRequest : RequestContext
    {
        public string? TourGenericCode { get; init; }
        public int?    YearFloor       { get; init; }
    }

    private sealed record RuleSaveRequest : RequestContext
    {
        public ScopeDto?             Scope       { get; init; }
        public string?               BaseVersion { get; init; }
        public List<RuleSaveChange>? Changes     { get; init; }
    }

    private sealed record ScopeDto
    {
        public string? Level           { get; init; }
        public string? ScopeKey        { get; init; }
        public string? TourGenericCode { get; init; }
        public string? TourSeriesCode  { get; init; }
        public string? DepartureId     { get; init; }
    }

    private sealed record RuleSaveChange(
        string  GroupTaskCode,
        string  ReferenceDate,
        CellDto Old,
        CellDto New);

    private sealed record AuditRequest : RequestContext
    {
        public string? ScopeKey { get; init; }
        public int?    Page     { get; init; }
        public int?    PageSize { get; init; }
    }

    private sealed record CodesAvailableRequest : RequestContext;

    private sealed record CellDto(string Kind, int? OffsetDays)
    {
        internal static CellDto Inherit       { get; } = new("inherit", null);
        internal static CellDto Na            { get; } = new("na",      null);
        internal static CellDto Set(int days) => new("set", days);
    }

    private sealed record ConflictDetail(
        string  ReferenceDate,
        string  GroupTaskCode,
        CellDto YourOld,
        CellDto Current);

    private sealed record TaskTemplateRow(string Code, string Name, string Source);

    private sealed record ParsedScopeKey(
        string  InternalType,
        string? TourGenericCode,
        string? SeriesCode,
        string? DepartureId);
}
