-- ============================================================
-- Nova.OpsGroups.Api — V001
-- MariaDB dialect
-- All tables live in the presets database (owned by Nova.Presets.Api).
-- enquiry_events, tour_series, sla_task, sla_task_audit
-- are in Nova.Presets.Api V006.
-- Note: MariaDB does not support partial/filtered indexes.
-- ============================================================

CREATE DATABASE IF NOT EXISTS `presets`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

-- ------------------------------------------------------------
-- tour_departures
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`tour_departures`
(
    `id`                    CHAR(36)       NOT NULL,
    `tenant_id`             VARCHAR(10)    NOT NULL,
    `departure_id`          VARCHAR(50)    NOT NULL,
    `series_code`           VARCHAR(20)    NOT NULL,
    `series_name`           VARCHAR(200)   NOT NULL,
    `departure_date`        DATE           NOT NULL,
    `return_date`           DATE               NULL,
    `destination_code`      VARCHAR(20)    NOT NULL,
    `destination_name`      VARCHAR(200)   NOT NULL,
    `branch_code`           VARCHAR(10)    NOT NULL,
    `ops_manager_initials`  VARCHAR(10)    NOT NULL DEFAULT '',
    `ops_manager_name`      VARCHAR(200)   NOT NULL DEFAULT '',
    `ops_exec_initials`     VARCHAR(10)    NOT NULL DEFAULT '',
    `ops_exec_name`         VARCHAR(200)   NOT NULL DEFAULT '',
    `pax_count`             INT            NOT NULL DEFAULT 0,
    `booking_count`         INT            NOT NULL DEFAULT 0,
    `gtd`                   TINYINT(1)     NOT NULL DEFAULT 0,
    `notes`                 VARCHAR(2000)      NULL,
    `frz_ind`               TINYINT(1)     NOT NULL DEFAULT 0,
    `created_by`            VARCHAR(10)    NOT NULL,
    `created_on`            DATETIME(6)    NOT NULL,
    `updated_by`            VARCHAR(10)    NOT NULL,
    `updated_on`            DATETIME(6)    NOT NULL,
    `updated_at`            VARCHAR(50)    NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_tour_departures_id` (`tenant_id`, `departure_id`)
) ENGINE=InnoDB;

CREATE INDEX `ix_tour_departures_date`
    ON `presets`.`tour_departures` (`tenant_id`, `departure_date`);

CREATE INDEX `ix_tour_departures_series`
    ON `presets`.`tour_departures` (`tenant_id`, `series_code`);

CREATE INDEX `ix_tour_departures_branch`
    ON `presets`.`tour_departures` (`tenant_id`, `branch_code`);

-- ------------------------------------------------------------
-- grouptour_departure_group_tasks
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`grouptour_departure_group_tasks`
(
    `id`              CHAR(36)       NOT NULL,
    `tenant_id`       VARCHAR(10)    NOT NULL,
    `departure_id`    VARCHAR(50)    NOT NULL,
    `group_task_id`   VARCHAR(50)    NOT NULL,
    `template_code`   VARCHAR(10)    NOT NULL,
    `status`          VARCHAR(30)    NOT NULL DEFAULT 'not_started',
    `due_date`        DATE               NULL,
    `completed_date`  DATE               NULL,
    `notes`           VARCHAR(2000)      NULL,
    `source`          VARCHAR(10)    NOT NULL DEFAULT 'GLOBAL',
    `frz_ind`         TINYINT(1)     NOT NULL DEFAULT 0,
    `created_by`      VARCHAR(10)    NOT NULL,
    `created_on`      DATETIME(6)    NOT NULL,
    `updated_by`      VARCHAR(10)    NOT NULL,
    `updated_on`      DATETIME(6)    NOT NULL,
    `updated_at`      VARCHAR(50)    NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_grouptour_departure_group_tasks_id` (`tenant_id`, `departure_id`, `group_task_id`)
) ENGINE=InnoDB;

CREATE INDEX `ix_grouptour_departure_group_tasks_dep`
    ON `presets`.`grouptour_departure_group_tasks` (`tenant_id`, `departure_id`);

-- ------------------------------------------------------------
-- grouptour_task_business_rules
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`grouptour_task_business_rules`
(
    `tenant_id`               VARCHAR(10)   NOT NULL,
    `company_code`            VARCHAR(10)   NOT NULL,
    `branch_code`             VARCHAR(10)   NOT NULL,
    `overdue_critical_days`   INT           NOT NULL DEFAULT 3,
    `overdue_warning_days`    INT           NOT NULL DEFAULT 7,
    `readiness_method`        VARCHAR(32)   NOT NULL DEFAULT 'required_only',
    `risk_red_threshold`      VARCHAR(64)   NOT NULL DEFAULT 'critical_overdue',
    `risk_amber_threshold`    VARCHAR(64)   NOT NULL DEFAULT 'any_overdue',
    `risk_green_threshold`    VARCHAR(64)   NOT NULL DEFAULT 'no_overdue',
    `heatmap_red_max`         INT           NOT NULL DEFAULT 39,
    `heatmap_amber_max`       INT           NOT NULL DEFAULT 79,
    `auto_mark_overdue`       TINYINT(1)    NOT NULL DEFAULT 1,
    `include_na_in_readiness` TINYINT(1)    NOT NULL DEFAULT 0,
    `updated_at`              DATETIME(6)   NOT NULL,
    `updated_by`              VARCHAR(10)   NOT NULL,
    PRIMARY KEY (`tenant_id`, `company_code`, `branch_code`)
) ENGINE=InnoDB;
