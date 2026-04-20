-- ============================================================
-- Nova.Presets.Api — V003
-- MariaDB dialect
-- Note: MariaDB does not support partial/filtered indexes.
-- ============================================================

-- ------------------------------------------------------------
-- programs
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`programs`
(
    `id`                      VARCHAR(10)     NOT NULL,
    `name`                    VARCHAR(200)    NOT NULL,
    `nav_type`                VARCHAR(20)     NOT NULL,
    `route`                   VARCHAR(150)    NULL,
    `external_url`            VARCHAR(150)    NULL,
    `external_url_param_mode` VARCHAR(150)    NOT NULL DEFAULT 'none',
    `icon`                    VARCHAR(100)    NULL,
    `is_active`               TINYINT(1)      NOT NULL DEFAULT 1,
    `frz_ind`                 TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`              VARCHAR(10)     NOT NULL,
    `created_on`              DATETIME        NOT NULL,
    `updated_by`              VARCHAR(10)     NOT NULL,
    `updated_on`              DATETIME        NOT NULL,
    `updated_at`              VARCHAR(50)     NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- program_tree
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`program_tree`
(
    `program_id_parent`   VARCHAR(10)     NOT NULL,
    `program_id_child`    VARCHAR(10)     NOT NULL,
    `sort_order`          INT             NOT NULL DEFAULT 0,
    `frz_ind`             TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`          VARCHAR(10)     NOT NULL,
    `created_on`          DATETIME        NOT NULL,
    `updated_by`          VARCHAR(10)     NOT NULL,
    `updated_on`          DATETIME        NOT NULL,
    `updated_at`          VARCHAR(50)     NOT NULL,
    PRIMARY KEY (`program_id_parent`, `program_id_child`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- Indexes
-- Note: MariaDB does not support partial/filtered indexes;
-- application filters frz_ind = 0 in WHERE clause.
-- ------------------------------------------------------------

CREATE INDEX `ix_program_tree_parent`
    ON `presets`.`program_tree` (`program_id_parent`, `sort_order`);
