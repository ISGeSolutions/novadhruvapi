-- ============================================================
-- Nova.CommonUX.Api — V006
-- MariaDB dialect
-- Adds page_config table to nova_auth database.
-- ============================================================

CREATE TABLE IF NOT EXISTS `nova_auth`.`page_config`
(
    `program_code`  VARCHAR(100)  NOT NULL,
    `config_json`   LONGTEXT          NULL,
    `updated_by`    VARCHAR(10)   NOT NULL DEFAULT '',
    `updated_on`    DATETIME(6)   NOT NULL,
    PRIMARY KEY (`program_code`)
) ENGINE=InnoDB;
