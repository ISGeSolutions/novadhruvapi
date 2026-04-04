namespace Nova.ToDo.Api.Models;

// ---------------------------------------------------------------------------
// Dapper DTO for list queries — includes joined description fields.
//
// Note: join SQL differs across MSSQL, Postgres, and MariaDB.
// The JOIN expressions in each list endpoint are marked with
// "-- JOIN: MSSQL" comments; rewrite for other engines.
//
// Legacy cross-database lookups (sales97.dbo.Priority, etc.) are MSSQL-only.
// Postgres and MariaDB schemas will have the lookup data in the service database.
// ---------------------------------------------------------------------------

internal sealed record ToDoListRow(
    int       SeqNo,
    string    PriorityCode,
    DateTime? StartDate,
    DateTime  DueDate,
    string    AssignedToUserCode,
    string    TaskDetail,
    string?   Remark,
    string    CreatedBy,
    DateTime  CreatedOn,
    string    UpdatedBy,
    DateTime  UpdatedOn,
    bool      SendSMSInd,
    string?   SendSMSTo,
    bool?     SentMailInd,
    bool      DoneInd,
    string?   AccountcodeClient,  // DB: Accountcode_Client
    int?      BkgNo,
    int?      QuoteNo,
    bool      FrzInd,
    // Joined description fields
    string?   PriorityName,
    string?   AssignedToUserName,
    string?   CreatedByName,
    string?   UpdatedByName,
    string?   ClientName,
    string?   TourCode,
    string?   ItineraryName
);
