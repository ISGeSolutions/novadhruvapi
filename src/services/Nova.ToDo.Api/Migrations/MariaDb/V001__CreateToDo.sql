-- V001: Create ToDo table (MariaDB / MySQL)
-- Nova.ToDo.Api — task management.
-- Safe to run automatically — CREATE TABLE IF NOT EXISTS, no destructive operations.

CREATE TABLE IF NOT EXISTS `todo` (
    `seq_no`                INT           NOT NULL AUTO_INCREMENT,
    `job_code`              VARCHAR(4)    NOT NULL,
    `task_detail`           VARCHAR(255)  NOT NULL,
    `assigned_to_user_code` VARCHAR(10)   NOT NULL,
    `priority_code`         VARCHAR(2)    NOT NULL,
    `due_date`              DATETIME      NOT NULL,
    `due_time`              DATETIME      NULL,
    `inflexible_ind`        TINYINT(1)    NOT NULL DEFAULT 0,
    `start_date`            DATETIME      NULL,
    `start_time`            DATETIME      NULL,
    `assigned_by_user_code` VARCHAR(10)   NOT NULL,
    `assigned_on`           DATETIME      NULL,
    `remark`                TEXT          NULL,
    `est_job_time`          DATETIME      NULL,

    `client_name`           VARCHAR(60)   NULL,
    `bkg_no`                INT           NULL,
    `quote_no`              INT           NULL,
    `campaign_code`         VARCHAR(16)   NULL,
    `account_code_client`   VARCHAR(10)   NULL,
    `tour_series_code`      VARCHAR(10)   NULL,
    `dep_date`              DATETIME      NULL,
    `supplier_code`         VARCHAR(10)   NULL,

    `send_email_to_ind`     TINYINT(1)    NOT NULL DEFAULT 0,
    `sent_mail_ind`         TINYINT(1)    NULL,
    `alert_to_ind`          TINYINT(1)    NOT NULL DEFAULT 0,
    `send_sms_ind`          TINYINT(1)    NOT NULL DEFAULT 0,
    `send_sms_to`           VARCHAR(20)   NULL,

    `travel_pnr_no`         VARCHAR(25)   NULL,
    `seq_no_charges`        INT           NULL,
    `seq_no_acct_notes`     INT           NULL,
    `itinerary_no`          INT           NULL,

    `done_ind`              TINYINT(1)    NOT NULL DEFAULT 0,
    `done_by`               VARCHAR(10)   NULL,
    `done_on`               DATETIME      NULL,

    `frz_ind`               TINYINT(1)    NOT NULL DEFAULT 0,
    `created_by`            VARCHAR(10)   NOT NULL,
    `created_on`            DATETIME      NOT NULL,
    `updated_by`            VARCHAR(10)   NOT NULL,
    `updated_on`            DATETIME      NOT NULL,
    `updated_at`            VARCHAR(50)   NOT NULL,

    PRIMARY KEY (`seq_no`),
    INDEX `ix_todo_assigned_to_user` (`assigned_to_user_code`, `done_ind`, `frz_ind`),
    INDEX `ix_todo_bkg_no`           (`bkg_no`),
    INDEX `ix_todo_quote_no`         (`quote_no`),
    INDEX `ix_todo_tour_series`      (`tour_series_code`, `dep_date`),
    INDEX `ix_todo_client`           (`account_code_client`),
    INDEX `ix_todo_supplier`         (`supplier_code`),
    INDEX `ix_todo_travel_pnr`       (`travel_pnr_no`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
