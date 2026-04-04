namespace Nova.ToDo.Api.Models;

// ---------------------------------------------------------------------------
// Full record response — returned by all pre-edit Get endpoints.
// Projected from ToDoRow via ToDoProjections.Project().
//
// Wire format: snake_case (handled by Nova JSON serialiser).
// Primary key is always a string on the wire ("seq_no": "1042").
// ---------------------------------------------------------------------------

internal sealed record ToDoDetail(
    string          SeqNo,               // int → string on wire
    string          JobCode,
    string          TaskDetail,
    string          AssignedToUserCode,
    string          PriorityCode,
    DateOnly        DueDate,             // wire: "2026-10-01"
    string?         DueTime,             // wire: "10:30"  (HH:mm)
    bool            InFlexibleInd,
    DateOnly?       StartDate,           // wire: "2026-09-15" or null
    string?         StartTime,           // wire: "09:00"  or null
    string          AssignedByUserCode,
    DateTimeOffset? AssignedOn,          // wire: "2026-04-01T09:00:00Z"
    string?         Remark,
    string?         EstJobTime,          // wire: "01:30"  (HH:mm — duration, not a clock time)
    string?         ClientName,
    int?            BkgNo,
    int?            QuoteNo,
    string?         CampaignCode,
    string?         AccountCodeClient,   // wire: account_code_client
    string?         TourSeriesCode,      // wire: tour_series_code  (DB: Brochure_Code_Short)
    DateOnly?       DepDate,             // wire: "2026-12-15" or null
    string?         SupplierCode,
    bool            SendEMailToInd,
    bool?           SentMailInd,
    bool            AlertToInd,
    bool            SendSMSInd,
    string?         SendSMSTo,
    string?         TravelPrnNo,         // wire: travel_pnr_no
    int?            SeqNoCharges,        // wire: seq_no_charges
    int?            SeqNoAcctNotes,      // wire: seq_no_acct_notes
    int?            ItineraryNo,         // wire: itinerary_no
    bool            DoneInd,
    string?         DoneBy,
    DateTimeOffset? DoneOn,              // wire: "2026-04-03T14:22:00Z"
    bool            FrzInd,
    string          CreatedBy,
    DateTimeOffset  CreatedOn,           // wire: "2026-01-10T08:30:00Z"
    string          UpdatedBy,
    DateTimeOffset  UpdatedOn,           // wire: "2026-04-03T14:22:00Z"
    string          UpdatedAt            // client IP, wire: "192.168.1.100"
);
