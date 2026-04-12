-- V002: Add outbox relay columns (MariaDB)
-- Adds broker routing fields and status tracking to `dhruvlog`.`nova_outbox`.
-- All statements are ALTER TABLE ADD COLUMN — allowed by default migration policy.

ALTER TABLE `dhruvlog`.`nova_outbox` ADD COLUMN exchange       VARCHAR(200)  NOT NULL DEFAULT '';
ALTER TABLE `dhruvlog`.`nova_outbox` ADD COLUMN routing_key    VARCHAR(200)  NOT NULL DEFAULT '';
ALTER TABLE `dhruvlog`.`nova_outbox` ADD COLUMN content_type   VARCHAR(100)  NOT NULL DEFAULT 'application/json';
ALTER TABLE `dhruvlog`.`nova_outbox` ADD COLUMN max_retries    INT           NOT NULL DEFAULT 5;
ALTER TABLE `dhruvlog`.`nova_outbox` ADD COLUMN status         VARCHAR(20)   NOT NULL DEFAULT 'pending';
ALTER TABLE `dhruvlog`.`nova_outbox` ADD COLUMN scheduled_on   DATETIME(6)   NULL;
ALTER TABLE `dhruvlog`.`nova_outbox` ADD COLUMN correlation_id VARCHAR(100)  NULL;

-- Index for the relay's pending-message poll.
-- MariaDB does not support partial indexes; index all rows and filter in the query.
-- The V001 index (IX_nova_outbox_unprocessed) remains for backward compatibility.
CREATE INDEX IX_nova_outbox_pending ON `dhruvlog`.`nova_outbox` (status, created_at);
