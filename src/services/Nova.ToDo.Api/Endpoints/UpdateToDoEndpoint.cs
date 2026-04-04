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
/// Partial update: <c>PATCH /api/v1/todos/{seq_no}</c>
/// Send only the fields being changed plus updated_on for concurrency.
/// Task-source fields are immutable and are silently ignored if included.
/// Returns 200 with seq_no + updated_on, or 204 if submitted data is identical to DB.
/// </summary>
public static class UpdateToDoEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapMethods("/todos/{seqNo:int}", ["PATCH"], HandleAsync)
             .RequireAuthorization()
             .WithName("ToDoUpdate");
    }

    private static async Task<IResult> HandleAsync(
        int                                   seqNo,
        UpdateToDoRequest                     request,
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

        if (request.SendSMSInd == true && string.IsNullOrWhiteSpace(request.SendSMSTo))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["send_sms_to"] = ["send_sms_to is required when send_sms_ind is true."] },
                title: "Validation failed");

        // TODO: rights check — user must have update rights for ToDo
        // TODO: lookup validations (422) for any populated fields
        //       Lookup table refs: dialect.TableRef("sales97", "jobs"), dialect.TableRef("sales97", "users"), etc.

        string todo = dialect.TableRef("sales97", "ToDo");

        string fetchSql = $"""
            SELECT SeqNo, JobCode, TaskDetail, AssignedToUserCode, PriorityCode,
                   DueDate, DueTime, InFlexibleInd, StartDate, StartTime,
                   AssignedByUserCode, Remark, EstJobTime,
                   ClientName, BkgNo, QuoteNo, CampaignCode, Accountcode_Client,
                   Brochure_Code_Short, DepDate, SupplierCode,
                   SendEMailToInd, AlertToInd, SendSMSInd, SendSMSTo,
                   DoneInd, FrzInd, UpdatedOn
            FROM {todo}
            WHERE SeqNo = @SeqNo
            """;

        using IDbConnection connection = connectionFactory.CreateForTenant(tenantContext);

        CurrentRow? current = await connection.QuerySingleOrDefaultAsync<CurrentRow>(
            fetchSql, new { SeqNo = seqNo }, commandTimeout: 30);

        if (current is null)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     $"ToDo record with seq_no {seqNo} was not found.",
                statusCode: StatusCodes.Status404NotFound);

        // Concurrency check
        ConcurrencySettings concurrency = concurrencyOptions.Value;
        if (concurrency.StrictMode)
        {
            DateTimeOffset dbUpdatedOn = new(DateTime.SpecifyKind(current.UpdatedOn, DateTimeKind.Utc));
            if (dbUpdatedOn > request.UpdatedOn)
                return TypedResults.Problem(
                    title:      "Conflict",
                    detail:     concurrency.ConflictMessage,
                    statusCode: StatusCodes.Status409Conflict);
        }

        // Build SET clause from only the fields that were sent (non-null = submitted).
        var setClauses = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("SeqNo",     seqNo);
        parameters.Add("UpdatedBy", request.UserId);
        parameters.Add("UpdatedAt", request.IpAddress ?? "unknown");

        void TrySet<T>(string col, string paramName, T? value) where T : struct
        {
            if (value.HasValue) { setClauses.Add($"{col} = @{paramName}"); parameters.Add(paramName, value.Value); }
        }
        void TrySetStr(string col, string paramName, string? value)
        {
            if (value is not null) { setClauses.Add($"{col} = @{paramName}"); parameters.Add(paramName, value); }
        }
        void TrySetBool(string col, string paramName, bool? value)
        {
            if (value.HasValue) { setClauses.Add($"{col} = @{paramName}"); parameters.Add(paramName, value.Value); }
        }

        TrySetStr("JobCode",             "JobCode",            request.JobCode);
        TrySetStr("TaskDetail",          "TaskDetail",         request.TaskDetail);
        TrySetStr("AssignedToUserCode",  "AssignedToUserCode", request.AssignedToUserCode);
        TrySetStr("AssignedByUserCode",  "AssignedByUserCode", request.AssignedByUserCode);
        TrySetStr("PriorityCode",        "PriorityCode",       request.PriorityCode);
        TrySetStr("Remark",              "Remark",             request.Remark);
        TrySetStr("ClientName",          "ClientName",         request.ClientName);
        TrySetStr("CampaignCode",        "CampaignCode",       request.CampaignCode);
        TrySetStr("Accountcode_Client",  "AccountCodeClient",  request.AccountCodeClient);
        TrySetStr("Brochure_Code_Short", "TourSeriesCode",     request.TourSeriesCode);
        TrySetStr("SupplierCode",        "SupplierCode",       request.SupplierCode);
        TrySetStr("SendSMSTo",           "SendSMSTo",          request.SendSMSTo);

        TrySet("DueDate",   "DueDate",   request.DueDate.HasValue    ? (DateTime?)request.DueDate.Value.ToDateTime(TimeOnly.MinValue)    : null);
        TrySet("StartDate", "StartDate", request.StartDate.HasValue  ? (DateTime?)request.StartDate.Value.ToDateTime(TimeOnly.MinValue)  : null);
        TrySet("DepDate",   "DepDate",   request.DepDate.HasValue    ? (DateTime?)request.DepDate.Value.ToDateTime(TimeOnly.MinValue)    : null);
        TrySet("BkgNo",     "BkgNo",     request.BkgNo);
        TrySet("QuoteNo",   "QuoteNo",   request.QuoteNo);
        TrySet("DueTime",   "DueTime",   request.DueTime.HasValue    ? (DateTime?)new DateTime(1900, 1, 1, request.DueTime.Value.Hour,   request.DueTime.Value.Minute,   0) : null);
        TrySet("StartTime", "StartTime", request.StartTime.HasValue  ? (DateTime?)new DateTime(1900, 1, 1, request.StartTime.Value.Hour, request.StartTime.Value.Minute, 0) : null);

        if (request.EstJobTime is not null)
        {
            DateTime? estJobTimeVal = ParseEstJobTime(request.EstJobTime);
            if (estJobTimeVal.HasValue) { setClauses.Add("EstJobTime = @EstJobTime"); parameters.Add("EstJobTime", estJobTimeVal.Value); }
        }

        TrySetBool("InFlexibleInd",  "InFlexibleInd",  request.InFlexibleInd);
        TrySetBool("SendEMailToInd", "SendEMailToInd",  request.SendEMailToInd);
        TrySetBool("AlertToInd",     "AlertToInd",      request.AlertToInd);
        TrySetBool("SendSMSInd",     "SendSMSInd",      request.SendSMSInd);

        if (setClauses.Count == 0)
            return TypedResults.NoContent();

        setClauses.Add("UpdatedBy = @UpdatedBy");
        setClauses.Add("UpdatedOn = GETUTCDATE()");   // TODO (item 3): MSSQL-only — replace per dialect
        setClauses.Add("UpdatedAt = @UpdatedAt");

        // TODO (item 3): GETUTCDATE() is MSSQL-only — use NOW() AT TIME ZONE 'UTC' for Postgres, UTC_TIMESTAMP() for MariaDB.
        // TODO (item 3): For Postgres, use RETURNING updated_on instead of SELECT GETUTCDATE().
        string updateSql = $"""
            UPDATE {todo}
            SET {string.Join(", ", setClauses)}
            WHERE SeqNo = @SeqNo;
            SELECT GETUTCDATE();
            """;

        DateTime newUpdatedOn = await connection.ExecuteScalarAsync<DateTime>(updateSql, parameters, commandTimeout: 30);
        DateTimeOffset updatedOnOffset = new(DateTime.SpecifyKind(newUpdatedOn, DateTimeKind.Utc));

        return TypedResults.Ok(new
        {
            seq_no     = seqNo.ToString(),
            updated_on = updatedOnOffset,
        });
    }

    private static DateTime? ParseEstJobTime(string? hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm)) return null;
        string[] parts = hhmm.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m))
            return new DateTime(1900, 1, 1, h, m, 0);
        return null;
    }

    private sealed record CurrentRow(
        int      SeqNo, DateTime UpdatedOn,
        string   JobCode, string TaskDetail, string AssignedToUserCode,
        string   PriorityCode, DateTime DueDate, bool InFlexibleInd,
        string   AssignedByUserCode, bool FrzInd, bool DoneInd);

    private sealed record UpdateToDoRequest : RequestContext
    {
        public DateTimeOffset UpdatedOn { get; init; }

        public string?   JobCode            { get; init; }
        public string?   TaskDetail         { get; init; }
        public string?   AssignedToUserCode { get; init; }
        public string?   AssignedByUserCode { get; init; }
        public string?   PriorityCode       { get; init; }
        public DateOnly? DueDate            { get; init; }
        public TimeOnly? DueTime            { get; init; }
        public bool?     InFlexibleInd      { get; init; }
        public DateOnly? StartDate          { get; init; }
        public TimeOnly? StartTime          { get; init; }
        public string?   Remark             { get; init; }
        public string?   EstJobTime         { get; init; }
        public string?   ClientName         { get; init; }
        public int?      BkgNo              { get; init; }
        public int?      QuoteNo            { get; init; }
        public string?   CampaignCode       { get; init; }
        public string?   AccountCodeClient  { get; init; }
        public string?   TourSeriesCode     { get; init; }
        public DateOnly? DepDate            { get; init; }
        public string?   SupplierCode       { get; init; }
        public bool?     SendEMailToInd     { get; init; }
        public bool?     AlertToInd         { get; init; }
        public bool?     SendSMSInd         { get; init; }
        public string?   SendSMSTo          { get; init; }
    }
}
