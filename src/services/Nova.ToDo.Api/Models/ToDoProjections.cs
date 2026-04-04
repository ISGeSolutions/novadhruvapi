namespace Nova.ToDo.Api.Models;

/// <summary>
/// Projects raw Dapper <see cref="ToDoRow"/> records into API response types.
/// Handles the MSSQL datetime → DateOnly / string "HH:mm" / DateTimeOffset conversions.
/// </summary>
internal static class ToDoProjections
{
    // -----------------------------------------------------------------------
    // MSSQL datetime convention:
    //   All datetime columns arrive from ADO.NET as DateTime (Kind = Unspecified).
    //   DateOnly columns: discard the time component via DateOnly.FromDateTime().
    //   Time-only columns (DueTime, StartTime, EstJobTime): format as "HH:mm".
    //   Audit/event timestamps: treat as UTC via DateTime.SpecifyKind(Utc),
    //   then wrap as DateTimeOffset.
    // -----------------------------------------------------------------------

    internal static ToDoDetail Project(ToDoRow r) => new(
        SeqNo:              r.SeqNo.ToString(),
        JobCode:            r.JobCode,
        TaskDetail:         r.TaskDetail,
        AssignedToUserCode: r.AssignedToUserCode,
        PriorityCode:       r.PriorityCode,
        DueDate:            DateOnly.FromDateTime(r.DueDate),
        DueTime:            r.DueTime.HasValue     ? r.DueTime.Value.ToString("HH:mm")     : null,
        InFlexibleInd:      r.InFlexibleInd,
        StartDate:          r.StartDate.HasValue   ? DateOnly.FromDateTime(r.StartDate.Value) : null,
        StartTime:          r.StartTime.HasValue   ? r.StartTime.Value.ToString("HH:mm")   : null,
        AssignedByUserCode: r.AssignedByUserCode,
        AssignedOn:         r.AssignedOn.HasValue  ? ToUtcOffset(r.AssignedOn.Value)        : null,
        Remark:             r.Remark,
        EstJobTime:         r.EstJobTime.HasValue  ? r.EstJobTime.Value.ToString("HH:mm")  : null,
        ClientName:         r.ClientName,
        BkgNo:              r.BkgNo,
        QuoteNo:            r.QuoteNo,
        CampaignCode:       r.CampaignCode,
        AccountCodeClient:  r.AccountcodeClient,
        TourSeriesCode:     r.BrochureCodeShort,
        DepDate:            r.DepDate.HasValue     ? DateOnly.FromDateTime(r.DepDate.Value) : null,
        SupplierCode:       r.SupplierCode,
        SendEMailToInd:     r.SendEMailToInd,
        SentMailInd:        r.SentMailInd,
        AlertToInd:         r.AlertToInd,
        SendSMSInd:         r.SendSMSInd,
        SendSMSTo:          r.SendSMSTo,
        TravelPrnNo:        r.TravelPNRNo,
        SeqNoCharges:       r.SeqNoCharges,
        SeqNoAcctNotes:     r.SeqNoAcctNotes,
        ItineraryNo:        r.ItineraryNo,
        DoneInd:            r.DoneInd,
        DoneBy:             r.DoneBy,
        DoneOn:             r.DoneOn.HasValue      ? ToUtcOffset(r.DoneOn.Value)            : null,
        FrzInd:             r.FrzInd,
        CreatedBy:          r.CreatedBy,
        CreatedOn:          ToUtcOffset(r.CreatedOn),
        UpdatedBy:          r.UpdatedBy,
        UpdatedOn:          ToUtcOffset(r.UpdatedOn),
        UpdatedAt:          r.UpdatedAt
    );

    private static DateTimeOffset ToUtcOffset(DateTime dt) =>
        new(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
}
