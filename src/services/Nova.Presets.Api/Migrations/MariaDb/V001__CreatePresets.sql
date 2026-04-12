-- ============================================================
-- Nova.Presets.Api — presets database
-- MariaDB dialect
-- presets is a database. All identifiers use backtick quoting.
-- Note: MariaDB does not support partial/filtered indexes.
-- ============================================================

CREATE DATABASE IF NOT EXISTS `presets`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

-- ------------------------------------------------------------
-- company
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`company`
(
    `company_code` VARCHAR(10)   NOT NULL,
    `tenant_id`    VARCHAR(10)  NOT NULL,
    `company_name` VARCHAR(50)  NOT NULL,
    `frz_ind`      TINYINT(1)   NOT NULL DEFAULT 0,
    PRIMARY KEY (`company_code`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- branch
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`branch`
(
    `branch_code`  VARCHAR(10)   NOT NULL,
    `company_code` VARCHAR(10)   NOT NULL,
    `branch_name`  VARCHAR(50)  NOT NULL,
    `frz_ind`      TINYINT(1)   NOT NULL DEFAULT 0,
    PRIMARY KEY (`branch_code`, `company_code`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_user_status
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`tenant_user_status`
(
    `tenant_id`    VARCHAR(10)   NOT NULL,
    `user_id`      VARCHAR(10)   NOT NULL,
    `status_id`    VARCHAR(50)   NOT NULL,
    `status_label` VARCHAR(200)  NOT NULL,
    `status_note`  VARCHAR(200)      NULL,
    `frz_ind`      TINYINT(1)    NOT NULL DEFAULT 0,
    `created_by`   VARCHAR(10)   NOT NULL,
    `created_on`   DATETIME      NOT NULL,
    `updated_by`   VARCHAR(10)   NOT NULL,
    `updated_on`   DATETIME      NOT NULL,
    `updated_at`   VARCHAR(50)  NOT NULL,
    PRIMARY KEY (`tenant_id`, `user_id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_password_change_requests
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`tenant_password_change_requests`
(
    `id`                 CHAR(36)      NOT NULL,
    `tenant_id`          VARCHAR(10)   NOT NULL,
    `user_id`            VARCHAR(10)   NOT NULL,
    `new_password_hash`  VARCHAR(500)  NOT NULL,
    `token_hash`         VARCHAR(500)  NOT NULL,
    `expires_on`         DATETIME      NOT NULL,
    `confirmed_on`       DATETIME          NULL,
    `created_on`         DATETIME      NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- Indexes
-- Note: MariaDB does not support partial/filtered indexes.
-- ix_presets_pcr_token covers all rows — application filters
-- confirmed_on IS NULL in the WHERE clause.
-- ------------------------------------------------------------

CREATE INDEX `ix_presets_pcr_token`
    ON `presets`.`tenant_password_change_requests` (`token_hash`);

CREATE INDEX `ix_presets_pcr_user`
    ON `presets`.`tenant_password_change_requests` (`tenant_id`, `user_id`);

CREATE INDEX `ix_presets_company_tenant`
    ON `presets`.`company` (`tenant_id`);
