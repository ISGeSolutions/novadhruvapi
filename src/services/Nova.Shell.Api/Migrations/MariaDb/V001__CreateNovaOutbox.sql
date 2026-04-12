-- ============================================================
-- Nova.Shell.Api — dhruvlog database
-- MariaDB dialect
-- dhruvlog is a database. All identifiers use backtick quoting.
-- Note: MariaDB does not support partial/filtered indexes.
-- ============================================================

CREATE DATABASE IF NOT EXISTS `dhruvlog`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

-- ------------------------------------------------------------
-- nova_outbox
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `dhruvlog`.`nova_outbox` (
    id              CHAR(36)          NOT NULL  DEFAULT (UUID()),
    aggregate_id    VARCHAR(100)      NOT NULL,
    event_type      VARCHAR(200)      NOT NULL,
    payload         LONGTEXT          NOT NULL,
    created_at      DATETIME(6)       NOT NULL  DEFAULT NOW(6),
    processed_at    DATETIME(6)       NULL,
    retry_count     INT               NOT NULL  DEFAULT 0,
    last_error      TEXT              NULL,
    CONSTRAINT pk_nova_outbox PRIMARY KEY (id)
) ENGINE=InnoDB;

CREATE INDEX ix_nova_outbox_unprocessed
    ON `dhruvlog`.`nova_outbox` (created_at);
