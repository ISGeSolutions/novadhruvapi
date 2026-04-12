-- V002: Add outbox relay columns (Postgres)
-- Adds broker routing fields and status tracking to nova_outbox.
-- All statements are ALTER TABLE ADD COLUMN — allowed by default migration policy.

ALTER TABLE dhruvlog.nova_outbox ADD COLUMN exchange       TEXT    NOT NULL DEFAULT '';
ALTER TABLE dhruvlog.nova_outbox ADD COLUMN routing_key    TEXT    NOT NULL DEFAULT '';
ALTER TABLE dhruvlog.nova_outbox ADD COLUMN content_type   TEXT    NOT NULL DEFAULT 'application/json';
ALTER TABLE dhruvlog.nova_outbox ADD COLUMN max_retries    INTEGER NOT NULL DEFAULT 5;
ALTER TABLE dhruvlog.nova_outbox ADD COLUMN status         TEXT    NOT NULL DEFAULT 'pending';
ALTER TABLE dhruvlog.nova_outbox ADD COLUMN scheduled_on   TIMESTAMPTZ NULL;
ALTER TABLE dhruvlog.nova_outbox ADD COLUMN correlation_id TEXT    NULL;

-- Index for the relay's pending-message poll.
-- The V001 index (ix_nova_outbox_unprocessed) remains for backward compatibility.
CREATE INDEX ix_nova_outbox_pending ON dhruvlog.nova_outbox (created_at) WHERE status = 'pending';
