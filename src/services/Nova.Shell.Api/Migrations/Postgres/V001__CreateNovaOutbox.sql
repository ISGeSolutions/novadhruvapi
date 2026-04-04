-- V001: Create Nova outbox table (Postgres)
-- Used by the outbox relay (Task 13) to guarantee at-least-once delivery of domain events.
-- Safe to run automatically — no destructive operations.

CREATE TABLE nova_outbox (
    id              UUID              NOT NULL  DEFAULT gen_random_uuid()  PRIMARY KEY,
    aggregate_id    VARCHAR(100)      NOT NULL,
    event_type      VARCHAR(200)      NOT NULL,
    payload         TEXT              NOT NULL,
    created_at      TIMESTAMPTZ       NOT NULL  DEFAULT NOW(),
    processed_at    TIMESTAMPTZ       NULL,
    retry_count     INTEGER           NOT NULL  DEFAULT 0,
    last_error      TEXT              NULL
);

CREATE INDEX IX_nova_outbox_unprocessed
    ON nova_outbox (created_at)
    WHERE processed_at IS NULL;
