-- V001: Create ToDo table (MSSQL)
-- Nova.ToDo.Api — task management for bookings, quotes, tour series, and clients.
-- Safe to run automatically — creates table only, no destructive operations.

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ToDo' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE sales97.dbo.ToDo (
        SeqNo               int IDENTITY(1,1) NOT NULL,
        JobCode             nvarchar(4)   NOT NULL,
        TaskDetail          nvarchar(255) NOT NULL,
        AssignedToUserCode  nvarchar(10)  NOT NULL,
        PriorityCode        nvarchar(2)   NOT NULL,
        DueDate             datetime      NOT NULL,
        DueTime             datetime      NULL,
        InFlexibleInd       bit           NOT NULL DEFAULT ((0)),
        StartDate           datetime      NULL,
        StartTime           datetime      NULL,
        AssignedByUserCode  nvarchar(10)  NOT NULL,
        AssignedOn          datetime      NULL,
        Remark              nvarchar(max) NULL,
        EstJobTime          datetime      NULL,

        ClientName          nvarchar(60)  NULL,
        BkgNo               int           NULL,
        QuoteNo             int           NULL,
        CampaignCode        nvarchar(16)  NULL,
        Accountcode_Client  nvarchar(10)  NULL,
        Brochure_Code_Short nvarchar(10)  NULL,
        DepDate             datetime      NULL,
        SupplierCode        nvarchar(10)  NULL,

        SendEMailToInd      bit           NOT NULL DEFAULT ((0)),
        SentMailInd         bit           NULL,
        AlertToInd          bit           NOT NULL DEFAULT ((0)),
        SendSMSInd          bit           NOT NULL DEFAULT ((0)),
        SendSMSTo           nvarchar(20)  NULL,

        Travel_PNRNo        nvarchar(25)  NULL,
        SeqNo_Charges       int           NULL,
        SeqNo_AcctNotes     int           NULL,
        Itinerary_No        int           NULL,

        DoneInd             bit           NOT NULL DEFAULT ((0)),
        DoneBy              nvarchar(10)  NULL,
        DoneOn              datetime      NULL,

        FrzInd              bit           NULL     DEFAULT ((0)),
        CreatedBy           nvarchar(10)  NOT NULL,
        CreatedOn           datetime      NOT NULL,
        UpdatedBy           nvarchar(10)  NOT NULL,
        UpdatedOn           datetime      NOT NULL,
        UpdatedAt           nvarchar(20)  NOT NULL,

        CONSTRAINT PK_ToDo PRIMARY KEY (SeqNo)
    );

    CREATE INDEX IX_ToDo_AssignedToUserCode ON sales97.dbo.ToDo (AssignedToUserCode, DoneInd, FrzInd);
    CREATE INDEX IX_ToDo_BkgNo              ON sales97.dbo.ToDo (BkgNo)   WHERE BkgNo IS NOT NULL;
    CREATE INDEX IX_ToDo_QuoteNo            ON sales97.dbo.ToDo (QuoteNo) WHERE QuoteNo IS NOT NULL;
    CREATE INDEX IX_ToDo_TourSeries         ON sales97.dbo.ToDo (Brochure_Code_Short, DepDate) WHERE Brochure_Code_Short IS NOT NULL;
    CREATE INDEX IX_ToDo_AccountcodeClient  ON sales97.dbo.ToDo (Accountcode_Client) WHERE Accountcode_Client IS NOT NULL;
    CREATE INDEX IX_ToDo_SupplierCode       ON sales97.dbo.ToDo (SupplierCode)       WHERE SupplierCode IS NOT NULL;
    CREATE INDEX IX_ToDo_Travel_PNRNo       ON sales97.dbo.ToDo (Travel_PNRNo)       WHERE Travel_PNRNo IS NOT NULL;
END
