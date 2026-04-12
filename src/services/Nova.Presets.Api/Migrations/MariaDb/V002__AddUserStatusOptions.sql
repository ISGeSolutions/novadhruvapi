-- ============================================================
-- Nova.Presets.Api — V002
-- MariaDB dialect
-- ============================================================

-- ------------------------------------------------------------
-- user_status_options
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`user_status_options`
(
    `id`           INT           NOT NULL AUTO_INCREMENT,
    `tenant_id`    VARCHAR(10)   NOT NULL,
    `company_code` VARCHAR(10)   NOT NULL,
    `branch_code`  VARCHAR(10)   NOT NULL,
    `status_code`  VARCHAR(50)   NOT NULL,
    `label`        VARCHAR(200)  NOT NULL,
    `colour`       VARCHAR(20)   NOT NULL,
    `serial_no`    INT           NOT NULL DEFAULT 0,
    `frz_ind`      TINYINT(1)    NOT NULL DEFAULT 0,
    `created_by`   VARCHAR(10)   NOT NULL,
    `created_on`   DATETIME      NOT NULL,
    `updated_by`   VARCHAR(10)   NOT NULL,
    `updated_on`   DATETIME      NOT NULL,
    `updated_at`   VARCHAR(50)   NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

CREATE INDEX `ix_uso_tenant_company_branch`
    ON `presets`.`user_status_options` (`tenant_id`, `company_code`, `branch_code`);
