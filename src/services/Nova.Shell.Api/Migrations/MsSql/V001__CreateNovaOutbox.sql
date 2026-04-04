-- V001: Create Nova outbox table (MSSQL)
-- Used by the outbox relay (Task 13) to guarantee at-least-once delivery of domain events.
-- Safe to run automatically — no destructive operations.

CREATE TABLE nova_outbox (
    id              UNIQUEIDENTIFIER  NOT NULL  DEFAULT NEWSEQUENTIALID()  CONSTRAINT PK_nova_outbox PRIMARY KEY,
    aggregate_id    NVARCHAR(100)     NOT NULL,
    event_type      NVARCHAR(200)     NOT NULL,
    payload         NVARCHAR(MAX)     NOT NULL,
    created_at      DATETIME2         NOT NULL  DEFAULT SYSUTCDATETIME(),
    processed_at    DATETIME2         NULL,
    retry_count     INT               NOT NULL  DEFAULT 0,
    last_error      NVARCHAR(MAX)     NULL
);

CREATE INDEX IX_nova_outbox_unprocessed
    ON nova_outbox (created_at)
    WHERE processed_at IS NULL;
