namespace Nova.ToDo.Api.Models;

// ---------------------------------------------------------------------------
// List response item — projected from ToDoListRow.
// Returned by all /list/* endpoints inside the ToDoPagedResult envelope.
// ---------------------------------------------------------------------------

internal sealed record ToDoListItem(
    string          SeqNo,              // int → string on wire
    string          PriorityCode,
    string?         PriorityName,
    DateOnly?       StartDate,          // wire: "2026-09-15" or null
    DateOnly        DueDate,            // wire: "2026-10-01"
    string          AssignedToUserCode,
    string?         AssignedToUserName,
    string          TaskDetail,
    string?         Remark,
    string          CreatedBy,
    string?         CreatedByName,
    DateTimeOffset  CreatedOn,          // wire: "2026-01-10T08:30:00Z"
    string          UpdatedBy,
    string?         UpdatedByName,
    DateTimeOffset  UpdatedOn,          // wire: "2026-04-03T14:22:00Z"
    bool            SendSMSInd,
    string?         SendSMSTo,
    bool?           SentMailInd,
    bool            DoneInd,
    string?         AccountCodeClient,  // wire: account_code_client
    string?         ClientName,
    int?            BkgNo,
    int?            QuoteNo,
    string?         TourCode,
    string?         ItineraryName,
    bool            FrzInd
);

/// <summary>Pagination envelope for all ToDo list endpoints.</summary>
internal sealed record ToDoPagedResult<T>(
    IEnumerable<T> Items,
    int            PageNo,
    int            PageSize,
    bool           HasNextPage);

internal static class ToDoListProjections
{
    internal static ToDoListItem Project(ToDoListRow r) => new(
        SeqNo:              r.SeqNo.ToString(),
        PriorityCode:       r.PriorityCode,
        PriorityName:       r.PriorityName,
        StartDate:          r.StartDate.HasValue ? DateOnly.FromDateTime(r.StartDate.Value) : null,
        DueDate:            DateOnly.FromDateTime(r.DueDate),
        AssignedToUserCode: r.AssignedToUserCode,
        AssignedToUserName: r.AssignedToUserName,
        TaskDetail:         r.TaskDetail,
        Remark:             r.Remark,
        CreatedBy:          r.CreatedBy,
        CreatedByName:      r.CreatedByName,
        CreatedOn:          new DateTimeOffset(DateTime.SpecifyKind(r.CreatedOn, DateTimeKind.Utc)),
        UpdatedBy:          r.UpdatedBy,
        UpdatedByName:      r.UpdatedByName,
        UpdatedOn:          new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedOn, DateTimeKind.Utc)),
        SendSMSInd:         r.SendSMSInd,
        SendSMSTo:          r.SendSMSTo,
        SentMailInd:        r.SentMailInd,
        DoneInd:            r.DoneInd,
        AccountCodeClient:  r.AccountcodeClient,
        ClientName:         r.ClientName,
        BkgNo:              r.BkgNo,
        QuoteNo:            r.QuoteNo,
        TourCode:           r.TourCode,
        ItineraryName:      r.ItineraryName,
        FrzInd:             r.FrzInd
    );

    /// <summary>
    /// Applies the page_size + 1 pattern: fetches one extra row to detect whether
    /// a next page exists, then discards it from the response.
    /// </summary>
    internal static ToDoPagedResult<T> BuildPage<T>(IEnumerable<T> rawRows, int pageNo, int pageSize)
    {
        List<T> rows = rawRows.ToList();
        bool hasNextPage = rows.Count > pageSize;
        if (hasNextPage) rows.RemoveAt(rows.Count - 1);
        return new ToDoPagedResult<T>(rows, pageNo, pageSize, hasNextPage);
    }
}
