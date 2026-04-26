namespace Nova.ToDo.Api.Models;

// ---------------------------------------------------------------------------
// Dapper DTO — mirrors the DB column layout of sales97.dbo.ToDo.
//
// MSSQL-LEGACY aliases applied in every SELECT query (see each endpoint):
//   FORMAT(DueTime, 'HH:mm')    AS due_time         → DueTime   (string?)
//   FORMAT(StartTime, 'HH:mm')  AS start_time       → StartTime (string?)
//   FORMAT(EstJobTime, 'HH:mm') AS est_job_time      → EstJobTime (string?)
//   Brochure_Code_Short         AS tour_series_code  → TourSeriesCode (string?)
//   ISNULL(FrzInd, 0)           AS frz_ind           → FrzInd (bool)
//
// Other legacy columns auto-map via MatchNamesWithUnderscores = true:
//   Accountcode_Client  → AccountcodeClient
//   Travel_PNRNo        → TravelPNRNo
//   SeqNo_Charges       → SeqNoCharges
//   SeqNo_AcctNotes     → SeqNoAcctNotes
//   Itinerary_No        → ItineraryNo
//
// Date-only datetime columns (DueDate, StartDate, DepDate, AssignedOn, DoneOn,
// CreatedOn, UpdatedOn) arrive as DateTime (Kind = Unspecified) and are
// projected to DateOnly / DateTimeOffset in ToDoDetail.
// ---------------------------------------------------------------------------

internal sealed record ToDoRow(
    int       SeqNo,
    string    JobCode,
    string    TaskDetail,
    string    AssignedToUserCode,
    string    PriorityCode,
    DateTime  DueDate,               // mandatory, date only (time component discarded)
    string?   DueTime,               // "HH:mm" — FORMAT(DueTime,'HH:mm') AS due_time
    bool      InFlexibleInd,
    DateTime? StartDate,             // optional, date only
    string?   StartTime,             // "HH:mm" — FORMAT(StartTime,'HH:mm') AS start_time
    string    AssignedByUserCode,
    DateTime? AssignedOn,            // UTC timestamp
    string?   Remark,
    string?   EstJobTime,            // "HH:mm" — FORMAT(EstJobTime,'HH:mm') AS est_job_time
    string?   ClientName,
    int?      BkgNo,
    int?      QuoteNo,
    string?   CampaignCode,
    string?   AccountcodeClient,     // DB: Accountcode_Client → wire: account_code_client
    string?   TourSeriesCode,        // DB: Brochure_Code_Short AS tour_series_code → wire: tour_series_code
    DateTime? DepDate,               // optional, date only
    string?   SupplierCode,
    bool      SendEMailToInd,
    bool?     SentMailInd,
    bool      AlertToInd,
    bool      SendSMSInd,
    string?   SendSMSTo,
    string?   TravelPNRNo,           // DB: Travel_PNRNo
    int?      SeqNoCharges,          // DB: SeqNo_Charges
    int?      SeqNoAcctNotes,        // DB: SeqNo_AcctNotes
    int?      ItineraryNo,           // DB: Itinerary_No
    bool      DoneInd,
    string?   DoneBy,
    DateTime? DoneOn,               // UTC timestamp
    bool      FrzInd,
    string    CreatedBy,
    DateTime  CreatedOn,            // UTC timestamp
    string    UpdatedBy,
    DateTime  UpdatedOn,            // UTC timestamp
    string    UpdatedAt,            // client IP address — not a timestamp
    int       LockVer               // optimistic concurrency token — see docs/concurrency-field-group-versioning.md
);
