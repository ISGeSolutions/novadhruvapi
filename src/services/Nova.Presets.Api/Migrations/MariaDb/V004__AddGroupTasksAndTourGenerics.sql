-- ============================================================
-- Nova.Presets.Api — V004
-- MariaDB dialect
-- Adds: group_tasks, tour_generics, tenant_user_permissions
-- ============================================================

CREATE TABLE IF NOT EXISTS `presets`.`group_tasks`
(
    `id`                          CHAR(36)        NOT NULL,
    `tenant_id`                   VARCHAR(10)     NOT NULL,
    `code`                        VARCHAR(10)     NOT NULL,
    `name`                        VARCHAR(200)    NOT NULL,
    `required`                    TINYINT(1)      NOT NULL DEFAULT 0,
    `critical`                    TINYINT(1)      NOT NULL DEFAULT 0,
    `group_task_sla_offset_days`  INT                 NULL,
    `reference_date`              VARCHAR(20)     NOT NULL DEFAULT 'departure',
    `source`                      VARCHAR(10)     NOT NULL DEFAULT 'GLOBAL',
    `sort_order`                  INT                 NULL,
    `frz_ind`                     TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`                  VARCHAR(10)     NOT NULL,
    `created_on`                  DATETIME(6)     NOT NULL,
    `updated_by`                  VARCHAR(10)     NOT NULL,
    `updated_on`                  DATETIME(6)     NOT NULL,
    `updated_at`                  VARCHAR(50)     NOT NULL,
    CONSTRAINT `pk_group_tasks` PRIMARY KEY (`id`),
    CONSTRAINT `uq_group_tasks_code` UNIQUE (`tenant_id`, `code`)
);

CREATE INDEX IF NOT EXISTS `ix_group_tasks_tenant`
    ON `presets`.`group_tasks` (`tenant_id`, `sort_order`, `code`);

CREATE TABLE IF NOT EXISTS `presets`.`tour_generics`
(
    `id`            CHAR(36)        NOT NULL,
    `tenant_id`     VARCHAR(10)     NOT NULL,
    `code`          VARCHAR(10)     NOT NULL,
    `name`          VARCHAR(200)    NOT NULL,
    `company_code`  VARCHAR(4)      NOT NULL,
    `branch_code`   VARCHAR(4)      NOT NULL,
    `frz_ind`       TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`  VARCHAR(10)     NOT NULL,
    `created_on`  DATETIME(6)     NOT NULL,
    `updated_by`  VARCHAR(10)     NOT NULL,
    `updated_on`  DATETIME(6)     NOT NULL,
    `updated_at`  VARCHAR(50)     NOT NULL,
    CONSTRAINT `pk_tour_generics` PRIMARY KEY (`id`),
    CONSTRAINT `uq_tour_generics_code` UNIQUE (`tenant_id`, `code`)
);

CREATE INDEX IF NOT EXISTS `ix_tour_generics_tenant`
    ON `presets`.`tour_generics` (`tenant_id`, `frz_ind`);

CREATE TABLE IF NOT EXISTS `presets`.`tenant_user_permissions`
(
    `tenant_id`       VARCHAR(10)     NOT NULL,
    `user_id`         VARCHAR(10)     NOT NULL,
    `permission_code` VARCHAR(100)    NOT NULL,
    CONSTRAINT `pk_tenant_user_permissions` PRIMARY KEY (`tenant_id`, `user_id`, `permission_code`)
);

CREATE INDEX IF NOT EXISTS `ix_tenant_user_permissions_user`
    ON `presets`.`tenant_user_permissions` (`tenant_id`, `user_id`);
