-- ============================================================
-- Nova.CommonUX.Api — nova_auth database
-- MariaDB dialect
-- nova_auth is a database. All identifiers use backtick quoting.
-- Note: MariaDB does not support partial/filtered indexes.
-- ============================================================

CREATE DATABASE IF NOT EXISTS `nova_auth`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

-- ------------------------------------------------------------
-- tenant_secrets
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `nova_auth`.`tenant_secrets`
(
    `tenant_id`           VARCHAR(10)     NOT NULL,
    `client_secret_hash`  VARCHAR(500)    NOT NULL,
    `frz_ind`             TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`          VARCHAR(10)     NOT NULL,
    `created_on`          DATETIME        NOT NULL,
    `updated_by`          VARCHAR(10)     NOT NULL,
    `updated_on`          DATETIME        NOT NULL,
    `updated_at`          VARCHAR(50)    NOT NULL,
    PRIMARY KEY (`tenant_id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_user_auth
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `nova_auth`.`tenant_user_auth`
(
    `tenant_id`               VARCHAR(10)     NOT NULL,
    `user_id`                 VARCHAR(10)     NOT NULL,
    `password_hash`           VARCHAR(500)    NULL,
    `totp_enabled`            TINYINT(1)      NOT NULL DEFAULT 0,
    `totp_secret_encrypted`   VARCHAR(500)    NULL,
    `failed_login_count`      INT             NOT NULL DEFAULT 0,
    `locked_until`            DATETIME        NULL,
    `last_login_on`           DATETIME        NULL,
    `frz_ind`                 TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`              VARCHAR(10)     NOT NULL,
    `created_on`              DATETIME        NOT NULL,
    `updated_by`              VARCHAR(10)     NOT NULL,
    `updated_on`              DATETIME        NOT NULL,
    `updated_at`              VARCHAR(50)    NOT NULL,
    PRIMARY KEY (`tenant_id`, `user_id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_user_profile
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `nova_auth`.`tenant_user_profile`
(
    `tenant_id`       VARCHAR(10)     NOT NULL,
    `user_id`         VARCHAR(10)     NOT NULL,
    `email`           VARCHAR(255)    NOT NULL,
    `display_name`    VARCHAR(200)    NOT NULL,
    `avatar_url`      VARCHAR(500)    NULL,
    `frz_ind`         TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`      VARCHAR(10)     NOT NULL,
    `created_on`      DATETIME        NOT NULL,
    `updated_by`      VARCHAR(10)     NOT NULL,
    `updated_on`      DATETIME        NOT NULL,
    `updated_at`      VARCHAR(50)    NOT NULL,
    PRIMARY KEY (`tenant_id`, `user_id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_user_social_identity
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `nova_auth`.`tenant_user_social_identity`
(
    `tenant_id`           VARCHAR(10)     NOT NULL,
    `user_id`             VARCHAR(10)     NOT NULL,
    `provider`            VARCHAR(50)     NOT NULL,
    `provider_user_id`    VARCHAR(255)    NULL,
    `provider_email`      VARCHAR(255)    NOT NULL,
    `linked_on`           DATETIME        NULL,
    `frz_ind`             TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`          VARCHAR(10)     NOT NULL,
    `created_on`          DATETIME        NOT NULL,
    `updated_by`          VARCHAR(10)     NOT NULL,
    `updated_on`          DATETIME        NOT NULL,
    `updated_at`          VARCHAR(50)    NOT NULL,
    PRIMARY KEY (`tenant_id`, `user_id`, `provider`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_auth_tokens
-- Immutable once written — no updated_* audit columns.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `nova_auth`.`tenant_auth_tokens`
(
    `id`          CHAR(36)        NOT NULL,
    `tenant_id`   VARCHAR(10)     NOT NULL,
    `user_id`     VARCHAR(10)     NOT NULL,
    `token_hash`  VARCHAR(500)    NOT NULL,
    `token_type`  VARCHAR(50)     NOT NULL,
    `expires_on`  DATETIME        NOT NULL,
    `used_on`     DATETIME        NULL,
    `created_on`  DATETIME        NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_config
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `nova_auth`.`tenant_config`
(
    `tenant_id`                       VARCHAR(10)     NOT NULL,
    `company_code`                      VARCHAR(10)     NOT NULL,
    `branch_code`                       VARCHAR(10)     NOT NULL,
    `tenant_name`                     VARCHAR(50)    NOT NULL,
    `company_name`                    VARCHAR(50)    NOT NULL,
    `branch_name`                     VARCHAR(50)    NOT NULL,
    `client_name`                     VARCHAR(200)    NOT NULL,
    `client_logo_url`                 VARCHAR(500)    NULL,
    `active_users_inline_threshold`   INT             NOT NULL DEFAULT 20,
    `unclosed_web_enquiries_url`      VARCHAR(500)    NULL,
    `task_list_url`                   VARCHAR(500)    NULL,
    `breadcrumb_position`             VARCHAR(50)     NOT NULL DEFAULT 'inline',
    `footer_gradient_refresh_ms`      INT             NOT NULL DEFAULT 300000,
    `frz_ind`                         TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`                      VARCHAR(10)     NOT NULL,
    `created_on`                      DATETIME        NOT NULL,
    `updated_by`                      VARCHAR(10)     NOT NULL,
    `updated_on`                      DATETIME        NOT NULL,
    `updated_at`                      VARCHAR(50)    NOT NULL,
    PRIMARY KEY (`tenant_id`, `company_code`, `branch_code`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_menu_items
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `nova_auth`.`tenant_menu_items`
(
    `menu_item_id`            VARCHAR(10)     NOT NULL,
    `tenant_id`               VARCHAR(10)     NOT NULL,
    `parent_id`               VARCHAR(10)     NULL,
    `label`                   VARCHAR(200)    NOT NULL,
    `icon`                    VARCHAR(100)    NULL,
    `route`                   VARCHAR(500)    NULL,
    `external_url_template`   VARCHAR(500)    NULL,
    `external_url_param_mode` VARCHAR(20)     NOT NULL DEFAULT 'none',
    `required_roles`          VARCHAR(500)    NULL,
    `sort_order`              INT             NOT NULL DEFAULT 0,
    `is_active`               TINYINT(1)      NOT NULL DEFAULT 1,
    `frz_ind`                 TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`              VARCHAR(10)     NOT NULL,
    `created_on`              DATETIME        NOT NULL,
    `updated_by`              VARCHAR(10)     NOT NULL,
    `updated_on`              DATETIME        NOT NULL,
    `updated_at`              VARCHAR(50)    NOT NULL,
    PRIMARY KEY (`menu_item_id`, `tenant_id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- Indexes
-- Note: MariaDB does not support partial/filtered indexes.
-- ix_tenant_user_profile_email covers all rows — application
--   must check frz_ind before raising a conflict.
-- ------------------------------------------------------------

CREATE UNIQUE INDEX `ix_tenant_user_profile_email`
    ON `nova_auth`.`tenant_user_profile` (`tenant_id`, `email`);

CREATE INDEX `ix_tenant_user_social_lookup`
    ON `nova_auth`.`tenant_user_social_identity` (`tenant_id`, `provider`, `provider_user_id`);

CREATE INDEX `ix_tenant_user_social_pending`
    ON `nova_auth`.`tenant_user_social_identity` (`tenant_id`, `provider`, `provider_email`);

CREATE INDEX `ix_tenant_auth_tokens_hash`
    ON `nova_auth`.`tenant_auth_tokens` (`token_hash`, `token_type`);

CREATE INDEX `ix_tenant_menu_items_tenant`
    ON `nova_auth`.`tenant_menu_items` (`tenant_id`, `sort_order`);
