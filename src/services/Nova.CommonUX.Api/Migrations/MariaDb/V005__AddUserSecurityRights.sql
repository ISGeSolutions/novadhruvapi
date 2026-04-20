-- ============================================================
-- Nova.CommonUX.Api — V005
-- MariaDB dialect
-- Adds user_security_rights and user_security_role_flag to nova_auth database.
-- company_code / branch_code = 'XXXX' means the assignment applies to all companies / branches.
-- ============================================================

CREATE TABLE IF NOT EXISTS `nova_auth`.`user_security_rights`
(
    `id`           CHAR(36)     NOT NULL,
    `tenant_id`    VARCHAR(10)  NOT NULL,
    `user_id`      VARCHAR(10)  NOT NULL,
    `role_code`    VARCHAR(10)  NOT NULL,
    `role_flags`   VARCHAR(16)  NOT NULL DEFAULT '',
    `company_code` VARCHAR(10)  NOT NULL DEFAULT 'XXXX',
    `branch_code`  VARCHAR(10)  NOT NULL DEFAULT 'XXXX',
    `frz_ind`      TINYINT(1)   NOT NULL DEFAULT 0,
    `created_by`   VARCHAR(10)  NOT NULL,
    `created_on`   DATETIME     NOT NULL,
    `updated_by`   VARCHAR(10)  NOT NULL,
    `updated_on`   DATETIME     NOT NULL,
    `updated_at`   VARCHAR(50)  NOT NULL,
    CONSTRAINT `pk_user_security_rights` PRIMARY KEY (`id`),
    CONSTRAINT `uq_user_security_rights` UNIQUE (`tenant_id`, `user_id`, `role_code`, `company_code`, `branch_code`)
);

CREATE TABLE IF NOT EXISTS `nova_auth`.`user_security_role_flag`
(
    `role_code`     VARCHAR(10)  NOT NULL,
    `flag_position` INT          NOT NULL,
    `flag_name`     VARCHAR(50)  NOT NULL,
    `flag_notes`    VARCHAR(200) NULL,
    CONSTRAINT `pk_user_security_role_flag` PRIMARY KEY (`role_code`, `flag_position`)
);
