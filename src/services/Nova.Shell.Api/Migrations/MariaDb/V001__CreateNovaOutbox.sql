-- V001: Create Nova outbox table (MariaDB / MySQL)
-- Used by the outbox relay (Task 13) to guarantee at-least-once delivery of domain events.
-- Safe to run automatically — no destructive operations.

CREATE TABLE nova_outbox (
    id              CHAR(36)          NOT NULL  DEFAULT (UUID()),
    aggregate_id    VARCHAR(100)      NOT NULL,
    event_type      VARCHAR(200)      NOT NULL,
    payload         LONGTEXT          NOT NULL,
    created_at      DATETIME(6)       NOT NULL  DEFAULT NOW(6),
    processed_at    DATETIME(6)       NULL,
    retry_count     INT               NOT NULL  DEFAULT 0,
    last_error      TEXT              NULL,
    CONSTRAINT PK_nova_outbox PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE INDEX IX_nova_outbox_unprocessed
    ON nova_outbox (created_at);
