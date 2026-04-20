-- ============================================================
-- Nova.OpsGroups.Api — opsgroups database
-- MariaDB dialect
-- opsgroups is a database. All identifiers use backtick quoting.
-- Note: MariaDB does not support partial/filtered indexes.
-- ============================================================

CREATE DATABASE IF NOT EXISTS `opsgroups`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

-- ------------------------------------------------------------
-- grouptour_departures
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `opsgroups`.`grouptour_departures`
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
    UNIQUE KEY `uq_grouptour_departures_id` (`tenant_id`, `departure_id`)
) ENGINE=InnoDB;

CREATE INDEX `ix_grouptour_departures_date`
    ON `opsgroups`.`grouptour_departures` (`tenant_id`, `departure_date`);

CREATE INDEX `ix_grouptour_departures_series`
    ON `opsgroups`.`grouptour_departures` (`tenant_id`, `series_code`);

CREATE INDEX `ix_grouptour_departures_branch`
    ON `opsgroups`.`grouptour_departures` (`tenant_id`, `branch_code`);

-- ------------------------------------------------------------
-- grouptour_departure_group_tasks
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `opsgroups`.`grouptour_departure_group_tasks`
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
    ON `opsgroups`.`grouptour_departure_group_tasks` (`tenant_id`, `departure_id`);

-- ------------------------------------------------------------
-- grouptour_sla_rules
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `opsgroups`.`grouptour_sla_rules`
(
    `id`                          CHAR(36)      NOT NULL,
    `tenant_id`                   VARCHAR(10)   NOT NULL,
    `level`                       VARCHAR(20)   NOT NULL,
    `scope_key`                   VARCHAR(100)  NOT NULL,
    `tour_code`                   VARCHAR(20)       NULL,
    `group_task_code`             VARCHAR(10)   NOT NULL,
    `reference_date`              VARCHAR(20)   NOT NULL,
    `group_task_sla_offset_days`  INT               NULL,
    `version`                     VARCHAR(50)       NULL,
    `created_by`                  VARCHAR(10)   NOT NULL,
    `created_on`                  DATETIME(6)   NOT NULL,
    `updated_by`                  VARCHAR(10)   NOT NULL,
    `updated_on`                  DATETIME(6)   NOT NULL,
    `updated_at`                  VARCHAR(50)   NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_grouptour_sla_rules` (`tenant_id`, `scope_key`, `group_task_code`, `reference_date`)
) ENGINE=InnoDB;

CREATE INDEX `ix_grouptour_sla_rules_level`
    ON `opsgroups`.`grouptour_sla_rules` (`tenant_id`, `level`, `tour_code`);

-- ------------------------------------------------------------
-- grouptour_sla_rule_audit
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `opsgroups`.`grouptour_sla_rule_audit`
(
    `id`                CHAR(36)      NOT NULL,
    `tenant_id`         VARCHAR(10)   NOT NULL,
    `scope_key`         VARCHAR(100)  NOT NULL,
    `scope_label`       VARCHAR(200)  NOT NULL,
    `group_task_code`   VARCHAR(10)   NOT NULL,
    `reference_date`    VARCHAR(20)   NOT NULL,
    `old_value`         INT               NULL,
    `new_value`         INT               NULL,
    `changed_by_name`   VARCHAR(200)  NOT NULL,
    `changed_at`        DATETIME(6)   NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

CREATE INDEX `ix_grouptour_sla_rule_audit_scope`
    ON `opsgroups`.`grouptour_sla_rule_audit` (`tenant_id`, `scope_key`, `changed_at`);

-- ------------------------------------------------------------
-- grouptour_task_business_rules
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `opsgroups`.`grouptour_task_business_rules`
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
