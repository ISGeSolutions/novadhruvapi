-- ============================================================
-- Nova.Shell.Api — dhruvlog schema
-- Postgres dialect
-- Connect to the target application database first, then run.
-- dhruvlog is a schema (not a separate database).
-- ============================================================

CREATE SCHEMA IF NOT EXISTS dhruvlog;

-- ------------------------------------------------------------
-- nova_outbox
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS dhruvlog.nova_outbox (
    id              UUID              NOT NULL  DEFAULT gen_random_uuid()  PRIMARY KEY,
    aggregate_id    VARCHAR(100)      NOT NULL,
    event_type      VARCHAR(200)      NOT NULL,
    payload         TEXT              NOT NULL,
    created_at      TIMESTAMPTZ       NOT NULL  DEFAULT NOW(),
    processed_at    TIMESTAMPTZ       NULL,
    retry_count     INTEGER           NOT NULL  DEFAULT 0,
    last_error      TEXT              NULL
);

CREATE INDEX ix_nova_outbox_unprocessed
    ON dhruvlog.nova_outbox (created_at)
    WHERE processed_at IS NULL;
