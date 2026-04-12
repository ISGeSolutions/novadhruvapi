-- ============================================================
-- Nova.Shell.Api — presets database
-- MSSQL dialect
-- ============================================================

-- ------------------------------------------------------------
-- nova_outbox
-- ------------------------------------------------------------
IF OBJECT_ID('dhruvlog.dbo.nova_outbox', 'U') IS NULL
BEGIN
    CREATE TABLE dhruvlog.dbo.nova_outbox (
        id              UNIQUEIDENTIFIER  NOT NULL  DEFAULT NEWSEQUENTIALID(),
        aggregate_id    NVARCHAR(100)     NOT NULL,
        event_type      NVARCHAR(200)     NOT NULL,
        payload         NVARCHAR(MAX)     NOT NULL,
        created_at      DATETIME2         NOT NULL  DEFAULT SYSUTCDATETIME(),
        processed_at    DATETIME2         NULL,
        retry_count     INT               NOT NULL  DEFAULT 0,
        last_error      NVARCHAR(MAX)     NULL,
        CONSTRAINT pk_nova_outbox PRIMARY KEY (id)
    );
END

CREATE INDEX ix_nova_outbox_unprocessed
    ON dhruvlog.dbo.nova_outbox (created_at)
    WHERE processed_at IS NULL;
