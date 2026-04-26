using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Requests;
using Nova.Shared.Tenancy;
using Nova.Shared.Validation;
using Nova.ToDo.Api.Models;

namespace Nova.ToDo.Api.Endpoints;

/// <summary>
/// Create: <c>POST /api/v1/todos</c>
///
/// Upsert logic when a task-source field is present:
/// <list type="bullet">
///   <item>Existing open record AND remark already in DB remark → 204 (no-op)</item>
///   <item>Existing open record AND remark not in DB remark → append remark, update differing fields → 201</item>
///   <item>Existing open record AND no remark sent → update differing non-task-source fields → 200</item>
///   <item>No existing open record → insert → 201</item>
/// </list>
///
/// If no task-source field is provided, always inserts → 201.
/// Task-source fields are immutable after insert and are never updated.
/// </summary>
public static class CreateToDoEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/todos", HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoCreate");
    }

    private static async Task<IResult> HandleAsync(
        CreateToDoRequest    request,
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

        // Required field validation
        var domainErrors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.JobCode))
            domainErrors["job_code"] = ["job_code is required."];
        if (string.IsNullOrWhiteSpace(request.TaskDetail))
            domainErrors["task_detail"] = ["task_detail is required."];
        if (string.IsNullOrWhiteSpace(request.AssignedToUserCode))
            domainErrors["assigned_to_user_code"] = ["assigned_to_user_code is required."];
        if (string.IsNullOrWhiteSpace(request.PriorityCode))
            domainErrors["priority_code"] = ["priority_code is required."];
        if (request.DueDate == default)
            domainErrors["due_date"] = ["due_date is required."];
        if (string.IsNullOrWhiteSpace(request.AssignedByUserCode))
            domainErrors["assigned_by_user_code"] = ["assigned_by_user_code is required."];
        if (request.SendSMSInd && string.IsNullOrWhiteSpace(request.SendSMSTo))
            domainErrors["send_sms_to"] = ["send_sms_to is required when send_sms_ind is true."];
        if (domainErrors.Count > 0)
            return TypedResults.ValidationProblem(domainErrors, title: "Validation failed");

        // Exactly one task-source field or none
        int taskSourceCount = CountTaskSourceFields(request);
        if (taskSourceCount > 1)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["task_source"] = ["Only one task-source field may be provided: travel_pnr_no, seq_no_charges, seq_no_acct_notes, or itinerary_no."]
                },
                title: "Validation failed");

        // TODO: rights check — user must have create rights for ToDo
        // TODO: lookup validations (422) for JobCode, AssignedToUserCode, AssignedByUserCode,
        //       PriorityCode, BkgNo, QuoteNo, CampaignCode, Accountcode_Client, tour_series_code,
        //       DepDate, SupplierCode, and task-source fields.
        //       Lookup table refs: dialect.TableRef("sales97", "jobs"), dialect.TableRef("sales97", "users"), etc.

        bool   hasTaskSource = taskSourceCount == 1;
        string todo          = dialect.TableRef("sales97", "ToDo");
        string doneOff       = dialect.BooleanLiteral(false);
        string clientIp      = request.IpAddress ?? "unknown";

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        if (hasTaskSource)
        {
            // Upsert path — search for an existing open record with the same task-source value.
            (string taskSourceCol, object taskSourceVal) = GetTaskSourceFilter(request);

            // TODO (item 2): SELECT TOP 1 is MSSQL-only — replace with LIMIT 1 for Postgres/MariaDB.
            // MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by rajeevjha on 14 Apr 2026.
            string findSql = $"""
                SELECT TOP 1 SeqNo, Remark, UpdatedOn, UpdatedBy, UpdatedAt,
                       JobCode, TaskDetail, AssignedToUserCode, PriorityCode, DueDate, DueTime,
                       InFlexibleInd, AssignedByUserCode
                FROM {todo}
                WHERE {taskSourceCol} = @TaskSourceVal
                  AND DoneInd = {doneOff}
                ORDER BY SeqNo DESC
                """;

            ExistingRow? existing = await connection.QuerySingleOrDefaultAsync<ExistingRow>(
                findSql, new { TaskSourceVal = taskSourceVal }, commandTimeout: 30);

            if (existing is not null)
            {
                if (!string.IsNullOrWhiteSpace(request.Remark))
                {
                    string dbRemark = existing.Remark ?? string.Empty;
                    if (dbRemark.Contains(request.Remark, StringComparison.OrdinalIgnoreCase))
                        return TypedResults.NoContent();   // Scenario A: remark already present

                    // Scenario B: append remark and update differing fields
                    string combinedRemark = string.IsNullOrEmpty(dbRemark)
                        ? request.Remark
                        : $"{dbRemark}; {request.Remark}";

                    await connection.ExecuteAsync(
                        BuildUpdateSql(todo, dialect, includeRemark: true),
                        BuildUpdateParams(request, existing.SeqNo, combinedRemark, clientIp),
                        commandTimeout: 30);

                    return TypedResults.Created(
                        (string?)null,
                        new { seq_no = existing.SeqNo.ToString() });
                }
                else
                {
                    // Scenario C: no remark — update differing non-task-source fields only
                    await connection.ExecuteAsync(
                        BuildUpdateSql(todo, dialect, includeRemark: false),
                        BuildUpdateParams(request, existing.SeqNo, existing.Remark, clientIp),
                        commandTimeout: 30);

                    return TypedResults.Ok(new { seq_no = existing.SeqNo.ToString() });
                }
            }
        }

        // Scenario D: insert new record
        // TODO (item 3): GETUTCDATE() is MSSQL-only — use NOW() AT TIME ZONE 'UTC' for Postgres, UTC_TIMESTAMP() for MariaDB.
        // TODO (item 4): SELECT CAST(SCOPE_IDENTITY() AS INT) is MSSQL-only — use RETURNING id for Postgres, SELECT LAST_INSERT_ID() for MariaDB.
        // MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by rajeevjha on 14 Apr 2026.
        string insertSql = $"""
            INSERT INTO {todo} (
                JobCode, TaskDetail, AssignedToUserCode, PriorityCode, DueDate, DueTime,
                InFlexibleInd, StartDate, StartTime, AssignedByUserCode, AssignedOn, Remark, EstJobTime,
                ClientName, BkgNo, QuoteNo, CampaignCode, Accountcode_Client,
                Brochure_Code_Short, DepDate, SupplierCode,
                SendEMailToInd, AlertToInd, SendSMSInd, SendSMSTo,
                Travel_PNRNo, SeqNo_Charges, SeqNo_AcctNotes, Itinerary_No,
                DoneInd, FrzInd,
                CreatedBy, CreatedOn, UpdatedBy, UpdatedOn, UpdatedAt
            ) VALUES (
                @JobCode, @TaskDetail, @AssignedToUserCode, @PriorityCode, @DueDate, @DueTime,
                @InFlexibleInd, @StartDate, @StartTime, @AssignedByUserCode, @AssignedOn, @Remark, @EstJobTime,
                @ClientName, @BkgNo, @QuoteNo, @CampaignCode, @AccountCodeClient,
                @TourSeriesCode, @DepDate, @SupplierCode,
                @SendEMailToInd, @AlertToInd, @SendSMSInd, @SendSMSTo,
                @TravelPrnNo, @SeqNoCharges, @SeqNoAcctNotes, @ItineraryNo,
                {doneOff}, {dialect.BooleanLiteral(false)},
                @UserId, GETUTCDATE(), @UserId, GETUTCDATE(), @ClientIp
            );
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;

        int newSeqNo = await connection.ExecuteScalarAsync<int>(
            insertSql,
            new
            {
                request.JobCode,
                request.TaskDetail,
                request.AssignedToUserCode,
                request.PriorityCode,
                DueDate           = request.DueDate.ToDateTime(TimeOnly.MinValue),
                DueTime           = request.DueTime.HasValue    ? (DateTime?)new DateTime(1900, 1, 1, request.DueTime.Value.Hour,    request.DueTime.Value.Minute,    0) : null,
                request.InFlexibleInd,
                StartDate         = request.StartDate.HasValue  ? (DateTime?)request.StartDate.Value.ToDateTime(TimeOnly.MinValue) : null,
                StartTime         = request.StartTime.HasValue  ? (DateTime?)new DateTime(1900, 1, 1, request.StartTime.Value.Hour, request.StartTime.Value.Minute,  0) : null,
                request.AssignedByUserCode,
                AssignedOn        = (DateTime?)DateTimeOffset.UtcNow.UtcDateTime,
                request.Remark,
                EstJobTime        = ParseEstJobTime(request.EstJobTime),
                request.ClientName,
                request.BkgNo,
                request.QuoteNo,
                request.CampaignCode,
                AccountCodeClient = request.AccountCodeClient,
                TourSeriesCode    = request.TourSeriesCode,
                DepDate           = request.DepDate.HasValue    ? (DateTime?)request.DepDate.Value.ToDateTime(TimeOnly.MinValue) : null,
                request.SupplierCode,
                request.SendEMailToInd,
                request.AlertToInd,
                request.SendSMSInd,
                request.SendSMSTo,
                TravelPrnNo       = request.TravelPrnNo,
                SeqNoCharges      = request.SeqNoCharges,
                SeqNoAcctNotes    = request.SeqNoAcctNotes,
                ItineraryNo       = request.ItineraryNo,
                UserId            = request.UserId,
                ClientIp          = clientIp,
            },
            commandTimeout: 30);

        return TypedResults.Created(
            (string?)null,
            new { seq_no = newSeqNo.ToString() });
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static int CountTaskSourceFields(CreateToDoRequest r) =>
        (r.TravelPrnNo    is not null ? 1 : 0) +
        (r.SeqNoCharges   is not null ? 1 : 0) +
        (r.SeqNoAcctNotes is not null ? 1 : 0) +
        (r.ItineraryNo    is not null ? 1 : 0);

    private static (string col, object val) GetTaskSourceFilter(CreateToDoRequest r) =>
        r.TravelPrnNo    is not null ? ("Travel_PNRNo",    (object)r.TravelPrnNo)    :
        r.SeqNoCharges   is not null ? ("SeqNo_Charges",   (object)r.SeqNoCharges)   :
        r.SeqNoAcctNotes is not null ? ("SeqNo_AcctNotes", (object)r.SeqNoAcctNotes) :
                                       ("Itinerary_No",    (object)r.ItineraryNo!);

    // MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by rajeevjha on 14 Apr 2026.
    // TODO (item 3): GETUTCDATE() is MSSQL-only — replace per dialect when adapting.
    private static string BuildUpdateSql(string todo, ISqlDialect dialect, bool includeRemark) =>
        $"""
        UPDATE {todo} SET
            JobCode            = @JobCode,
            TaskDetail         = @TaskDetail,
            AssignedToUserCode = @AssignedToUserCode,
            PriorityCode       = @PriorityCode,
            DueDate            = @DueDate,
            DueTime            = @DueTime,
            InFlexibleInd      = @InFlexibleInd,
            AssignedByUserCode = @AssignedByUserCode
            {(includeRemark ? ", Remark = @Remark" : string.Empty)},
            UpdatedBy          = @UpdatedBy,
            UpdatedOn          = GETUTCDATE(),
            UpdatedAt          = @UpdatedAt
        WHERE SeqNo = @SeqNo
        """;

    private static object BuildUpdateParams(CreateToDoRequest r, int seqNo, string? remark, string clientIp) => new
    {
        r.JobCode,
        r.TaskDetail,
        r.AssignedToUserCode,
        r.PriorityCode,
        DueDate    = r.DueDate.ToDateTime(TimeOnly.MinValue),
        DueTime    = r.DueTime.HasValue ? (DateTime?)new DateTime(1900, 1, 1, r.DueTime.Value.Hour, r.DueTime.Value.Minute, 0) : null,
        r.InFlexibleInd,
        r.AssignedByUserCode,
        Remark     = remark,
        SeqNo      = seqNo,
        UpdatedBy  = r.UserId,
        UpdatedAt  = clientIp,
    };

    private static DateTime? ParseEstJobTime(string? hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm)) return null;
        string[] parts = hhmm.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m))
            return new DateTime(1900, 1, 1, h, m, 0);
        return null;
    }

    private sealed record ExistingRow(int SeqNo, string? Remark, DateTime UpdatedOn, string UpdatedBy, string UpdatedAt,
        string JobCode, string TaskDetail, string AssignedToUserCode, string PriorityCode,
        DateTime DueDate, DateTime? DueTime, bool InFlexibleInd, string AssignedByUserCode);

    // ---------------------------------------------------------------------------
    // Request
    // ---------------------------------------------------------------------------
    private sealed record CreateToDoRequest : RequestContext
    {
        public string   JobCode             { get; init; } = string.Empty;
        public string   TaskDetail          { get; init; } = string.Empty;
        public string   AssignedToUserCode  { get; init; } = string.Empty;
        public string   PriorityCode        { get; init; } = string.Empty;
        public DateOnly DueDate             { get; init; }
        public string   AssignedByUserCode  { get; init; } = string.Empty;

        public TimeOnly? DueTime           { get; init; }
        public bool      InFlexibleInd     { get; init; }
        public DateOnly? StartDate         { get; init; }
        public TimeOnly? StartTime         { get; init; }
        public string?   Remark            { get; init; }
        public string?   EstJobTime        { get; init; }

        public string?   ClientName        { get; init; }
        public int?      BkgNo             { get; init; }
        public int?      QuoteNo           { get; init; }
        public string?   CampaignCode      { get; init; }
        public string?   AccountCodeClient { get; init; }
        public string?   TourSeriesCode    { get; init; }
        public DateOnly? DepDate           { get; init; }
        public string?   SupplierCode      { get; init; }

        public bool      SendEMailToInd    { get; init; }
        public bool      AlertToInd        { get; init; }
        public bool      SendSMSInd        { get; init; }
        public string?   SendSMSTo         { get; init; }

        public string?   TravelPrnNo       { get; init; }
        public int?      SeqNoCharges      { get; init; }
        public int?      SeqNoAcctNotes    { get; init; }
        public int?      ItineraryNo       { get; init; }
    }
}
