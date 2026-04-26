-- ============================================================
-- Nova.Presets.Api — V006
-- MariaDB dialect
-- Adds: enquiry_events (lookup + seed), tour_series,
--       sla_task, sla_task_audit
-- Note: MariaDB does not support partial/filtered indexes.
--       CHECK constraints are parsed but not enforced
--       before MariaDB 10.2.1; kept for documentation.
-- ============================================================

-- ------------------------------------------------------------
-- enquiry_events
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`enquiry_events`
(
    `code`        VARCHAR(5)    NOT NULL,
    `description` VARCHAR(100)  NOT NULL,
    `sort_order`  INT           NOT NULL,
    PRIMARY KEY (`code`)
) ENGINE=InnoDB;

INSERT IGNORE INTO `presets`.`enquiry_events` (`code`, `description`, `sort_order`) VALUES
    ('DP', 'Departure Date', 1),
    ('RT', 'Return Date',    2),
    ('JI', 'JI Date',        3);

-- ------------------------------------------------------------
-- tour_series
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`tour_series`
(
    `id`               CHAR(36)      NOT NULL,
    `tenant_id`        VARCHAR(10)   NOT NULL,
    `series_code`      VARCHAR(20)   NOT NULL,
    `series_name`      VARCHAR(200)  NOT NULL,
    `tour_generic_id`  CHAR(36)      NOT NULL,
    `frz_ind`          TINYINT(1)    NOT NULL DEFAULT 0,
    `created_by`       VARCHAR(10)   NOT NULL,
    `created_on`       DATETIME(6)   NOT NULL,
    `updated_by`       VARCHAR(10)   NOT NULL,
    `updated_on`       DATETIME(6)   NOT NULL,
    `updated_at`       VARCHAR(50)   NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_tour_series_code` (`tenant_id`, `series_code`),
    CONSTRAINT `fk_tour_series_tour_generics`
        FOREIGN KEY (`tour_generic_id`) REFERENCES `presets`.`tour_generics` (`id`)
) ENGINE=InnoDB;

CREATE INDEX `ix_tour_series_tenant`
    ON `presets`.`tour_series` (`tenant_id`, `frz_ind`);

-- ------------------------------------------------------------
-- sla_task
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`sla_task`
(
    `id`              CHAR(36)     NOT NULL,
    `tenant_id`       VARCHAR(10)  NOT NULL,
    `scope_type`      VARCHAR(4)   NOT NULL,
    `scope_id`        CHAR(36)     NOT NULL,
    `enq_event_code`  VARCHAR(5)   NOT NULL,
    `task_code`       VARCHAR(10)  NOT NULL,
    `kind`            VARCHAR(5)   NOT NULL,
    `offset_days`     INT              NULL,
    `updated_by`      VARCHAR(10)  NOT NULL,
    `updated_on`      DATETIME(6)  NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_sla_task_scope_event_task` (`tenant_id`, `scope_type`, `scope_id`, `enq_event_code`, `task_code`),
    CONSTRAINT `fk_sla_task_enquiry_events`
        FOREIGN KEY (`enq_event_code`) REFERENCES `presets`.`enquiry_events` (`code`),
    CONSTRAINT `chk_sla_task_scope_type` CHECK (`scope_type` IN ('GLOB','TG','TS','TD')),
    CONSTRAINT `chk_sla_task_kind`       CHECK (`kind` IN ('SET','NA')),
    CONSTRAINT `chk_sla_task_offset`     CHECK (
        (`kind` = 'SET' AND `offset_days` IS NOT NULL) OR
        (`kind` = 'NA'  AND `offset_days` IS NULL)
    )
) ENGINE=InnoDB;

CREATE INDEX `ix_sla_task_scope`
    ON `presets`.`sla_task` (`tenant_id`, `scope_type`, `scope_id`);

-- ------------------------------------------------------------
-- sla_task_audit
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`sla_task_audit`
(
    `id`               CHAR(36)     NOT NULL,
    `tenant_id`        VARCHAR(10)  NOT NULL,
    `scope_type`       VARCHAR(4)   NOT NULL,
    `scope_id`         CHAR(36)     NOT NULL,
    `enq_event_code`   VARCHAR(5)   NOT NULL,
    `task_code`        VARCHAR(10)  NOT NULL,
    `kind_old`         VARCHAR(5)       NULL,
    `offset_days_old`  INT              NULL,
    `kind_new`         VARCHAR(5)       NULL,
    `offset_days_new`  INT              NULL,
    `changed_by`       VARCHAR(10)  NOT NULL,
    `changed_on`       DATETIME(6)  NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

CREATE INDEX `ix_sla_task_audit_scope`
    ON `presets`.`sla_task_audit` (`tenant_id`, `scope_type`, `scope_id`, `changed_on`);
