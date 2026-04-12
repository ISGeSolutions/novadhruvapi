-- V002: Add outbox relay columns (MSSQL)
-- Adds broker routing fields and status tracking to nova_outbox.
-- All statements are ALTER TABLE ADD — allowed by default migration policy.

ALTER TABLE dhruvlog.dbo.nova_outbox ADD exchange       NVARCHAR(200) NOT NULL DEFAULT '';
ALTER TABLE dhruvlog.dbo.nova_outbox ADD routing_key    NVARCHAR(200) NOT NULL DEFAULT '';
ALTER TABLE dhruvlog.dbo.nova_outbox ADD content_type   NVARCHAR(100) NOT NULL DEFAULT 'application/json';
ALTER TABLE dhruvlog.dbo.nova_outbox ADD max_retries    INT           NOT NULL DEFAULT 5;
ALTER TABLE dhruvlog.dbo.nova_outbox ADD status         NVARCHAR(20)  NOT NULL DEFAULT 'pending';
ALTER TABLE dhruvlog.dbo.nova_outbox ADD scheduled_on   DATETIME2     NULL;
ALTER TABLE dhruvlog.dbo.nova_outbox ADD correlation_id NVARCHAR(100) NULL;

-- Index for the relay's pending-message poll.
-- The V001 index (IX_nova_outbox_unprocessed) remains for backward compatibility.
CREATE INDEX is_nova_outbox_pending ON dhruvlog.dbo.nova_outbox (created_at) WHERE status = 'pending';
