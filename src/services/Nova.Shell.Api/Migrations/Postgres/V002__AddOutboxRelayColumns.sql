-- V002: Add outbox relay columns (Postgres)
-- Adds broker routing fields and status tracking to nova_outbox.
-- All statements are ALTER TABLE ADD COLUMN — allowed by default migration policy.

ALTER TABLE nova_outbox ADD COLUMN exchange       TEXT    NOT NULL DEFAULT '';
ALTER TABLE nova_outbox ADD COLUMN routing_key    TEXT    NOT NULL DEFAULT '';
ALTER TABLE nova_outbox ADD COLUMN content_type   TEXT    NOT NULL DEFAULT 'application/json';
ALTER TABLE nova_outbox ADD COLUMN max_retries    INTEGER NOT NULL DEFAULT 5;
ALTER TABLE nova_outbox ADD COLUMN status         TEXT    NOT NULL DEFAULT 'pending';
ALTER TABLE nova_outbox ADD COLUMN scheduled_on   TIMESTAMPTZ NULL;
ALTER TABLE nova_outbox ADD COLUMN correlation_id TEXT    NULL;

-- Index for the relay's pending-message poll.
-- The V001 index (IX_nova_outbox_unprocessed) remains for backward compatibility.
CREATE INDEX IX_nova_outbox_pending ON nova_outbox (created_at) WHERE status = 'pending';
