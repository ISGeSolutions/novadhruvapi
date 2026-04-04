namespace Nova.ToDo.Api.Models;

// ---------------------------------------------------------------------------
// Dapper DTO — mirrors the DB column layout of sales97.dbo.ToDo.
//
// Dapper maps column names to property names case-insensitively and strips
// underscores, so legacy columns map cleanly:
//
//   Accountcode_Client  → AccountcodeClient
//   Brochure_Code_Short → BrochureCodeShort
//   Travel_PNRNo        → TravelPNRNo
//   SeqNo_Charges       → SeqNoCharges
//   SeqNo_AcctNotes     → SeqNoAcctNotes
//   Itinerary_No        → ItineraryNo
//
// All datetime columns from MSSQL arrive as DateTime (Kind = Unspecified).
// They are projected to DateOnly / string "HH:mm" / DateTimeOffset in ToDoDetail.
// ---------------------------------------------------------------------------

internal sealed record ToDoRow(
    int       SeqNo,
    string    JobCode,
    string    TaskDetail,
    string    AssignedToUserCode,
    string    PriorityCode,
    DateTime  DueDate,               // mandatory, date only (time component discarded)
    DateTime? DueTime,               // optional, time only  (date component discarded)
    bool      InFlexibleInd,
    DateTime? StartDate,             // optional, date only
    DateTime? StartTime,             // optional, time only
    string    AssignedByUserCode,
    DateTime? AssignedOn,            // UTC timestamp
    string?   Remark,
    DateTime? EstJobTime,            // duration stored as datetime — projected to "HH:mm"
    string?   ClientName,
    int?      BkgNo,
    int?      QuoteNo,
    string?   CampaignCode,
    string?   AccountcodeClient,     // DB: Accountcode_Client → wire: account_code_client
    string?   BrochureCodeShort,     // DB: Brochure_Code_Short → wire: tour_series_code
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
    string    UpdatedAt             // client IP address — not a timestamp
);
