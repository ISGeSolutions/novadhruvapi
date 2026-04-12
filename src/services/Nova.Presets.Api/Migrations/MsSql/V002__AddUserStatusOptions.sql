-- ============================================================
-- Nova.Presets.Api — V002
-- MSSQL dialect
-- ============================================================

-- ------------------------------------------------------------
-- user_status_options
-- ------------------------------------------------------------
IF OBJECT_ID('presets.dbo.user_status_options', 'U') IS NULL
BEGIN
    CREATE TABLE presets.dbo.user_status_options
    (
        id           int           NOT NULL IDENTITY(1,1),
        tenant_id    varchar(10)   NOT NULL,
        company_code varchar(10)   NOT NULL,
        branch_code  varchar(10)   NOT NULL,
        status_code  varchar(50)   NOT NULL,
        label        varchar(200)  NOT NULL,
        colour       varchar(20)   NOT NULL,
        serial_no    int           NOT NULL DEFAULT 0,
        frz_ind      bit           NOT NULL DEFAULT 0,
        created_by   varchar(10)   NOT NULL,
        created_on   datetime2     NOT NULL,
        updated_by   varchar(10)   NOT NULL,
        updated_on   datetime2     NOT NULL,
        updated_at   varchar(50)   NOT NULL,
        CONSTRAINT pk_user_status_options PRIMARY KEY (id)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE  name      = 'ix_uso_tenant_company_branch'
               AND    object_id = OBJECT_ID('presets.dbo.user_status_options'))
    CREATE INDEX ix_uso_tenant_company_branch
        ON presets.dbo.user_status_options (tenant_id, company_code, branch_code)
        WHERE frz_ind = 0;
GO

/*
{ "id": "available", "label": "Available", "colour": "#22c55e" },
  { "id": "busy", "label": "Busy", "colour": "#ef4444" },
  { "id": "in-meeting", "label": "In a Meeting", "colour": "#f59e0b" },
  { "id": "out-of-office", "label": "Out of Office", "colour": "#6b7280" },
  { "id": "dnd", "label": "Do Not Disturb", "colour": "#dc2626" }
]
insert into presets.dbo.user_status_options (
  tenant_id, company_code, branch_code, status_code, label, colour, serial_no, 
  frz_ind, created_by, created_on, updated_by, updated_on, updated_at )

select
  'BTDK', 'XXXX', 'XXXX', 'available', 'Available', '#22c55e', 0,
  0, 'AUTO', getdate(), 'AUTO', getdate(), 'PRESET'
union all
select
  'BTDK', 'XXXX', 'XXXX', 'busy', 'Busy', '#ef4444', 0,
  0, 'AUTO', getdate(), 'AUTO', getdate(), 'PRESET'
union all
select
  'BTDK', 'XXXX', 'XXXX', 'out-of-office', 'Out of Office', '#6b7280', 1,
  0, 'AUTO', getdate(), 'AUTO', getdate(), 'PRESET'
union all
select
  'BTDK', 'XXXX', 'XXXX', 'in-meeting', 'In a Meeting', '#f59e0b', 10,
  0, 'AUTO', getdate(), 'AUTO', getdate(), 'PRESET'
union all
select
  'BTDK', 'XXXX', 'XXXX', 'dnd', 'Do Not Disturb', '#dc2626', 10,
  0, 'AUTO', getdate(), 'AUTO', getdate(), 'PRESET'  
*/    