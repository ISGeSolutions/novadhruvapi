using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.ToDo.Api.Models;

namespace Nova.ToDo.Api.Endpoints;

internal static class ToDoDbHelper
{
    // MSSQL-LEGACY. Review aliases 14 Apr 2026. Reviewed by rajeevjha on 14 Apr 2026.
    internal static Task<ToDoRow?> FetchBySeqNoAsync(IDbConnection conn, ISqlDialect dialect, int seqNo) =>
        conn.QuerySingleOrDefaultAsync<ToDoRow?>(
            $"""
            SELECT
                SeqNo, JobCode, TaskDetail, AssignedToUserCode, PriorityCode,
                DueDate, FORMAT(DueTime, 'HH:mm') AS due_time, InFlexibleInd,
                StartDate, FORMAT(StartTime, 'HH:mm') AS start_time,
                AssignedByUserCode, AssignedOn, Remark,
                FORMAT(EstJobTime, 'HH:mm') AS est_job_time,
                ClientName, BkgNo, QuoteNo, CampaignCode,
                Accountcode_Client, Brochure_Code_Short AS tour_series_code, DepDate, SupplierCode,
                SendEMailToInd, SentMailInd, AlertToInd, SendSMSInd, SendSMSTo,
                Travel_PNRNo, SeqNo_Charges, SeqNo_AcctNotes, Itinerary_No,
                DoneInd, DoneBy, DoneOn,
                ISNULL(FrzInd, 0) AS frz_ind, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn, UpdatedAt,
                lock_ver
            FROM {dialect.TableRef("sales97", "ToDo")}
            WHERE SeqNo = @SeqNo
            """,
            new { SeqNo = seqNo },
            commandTimeout: 10);
}
