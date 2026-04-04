-- V001: Create ToDo table (Postgres)
-- Nova.ToDo.Api — task management.
-- Uses UUID v7 primary key (app-generated).
-- Schema: sales97 — mirrors the MSSQL database name per Nova naming convention:
--   MSSQL  sales97.dbo.ToDo  →  Postgres  sales97.todo  (database-name becomes schema-name, dbo dropped)
-- Safe to run automatically — CREATE TABLE IF NOT EXISTS, no destructive operations.

CREATE SCHEMA IF NOT EXISTS sales97;

CREATE TABLE IF NOT EXISTS sales97.todo (
    id                    uuid          NOT NULL,
    job_code              varchar(4)    NOT NULL,
    task_detail           varchar(255)  NOT NULL,
    assigned_to_user_code varchar(10)   NOT NULL,
    priority_code         varchar(2)    NOT NULL,
    due_date              date          NOT NULL,
    due_time              time          NULL,
    inflexible_ind        boolean       NOT NULL DEFAULT false,
    start_date            date          NULL,
    start_time            time          NULL,
    assigned_by_user_code varchar(10)   NOT NULL,
    assigned_on           timestamptz   NULL,
    remark                text          NULL,
    est_job_time          interval      NULL,

    client_name           varchar(60)   NULL,
    bkg_no                integer       NULL,
    quote_no              integer       NULL,
    campaign_code         varchar(16)   NULL,
    account_code_client   varchar(10)   NULL,
    tour_series_code      varchar(10)   NULL,
    dep_date              date          NULL,
    supplier_code         varchar(10)   NULL,

    send_email_to_ind     boolean       NOT NULL DEFAULT false,
    sent_mail_ind         boolean       NULL,
    alert_to_ind          boolean       NOT NULL DEFAULT false,
    send_sms_ind          boolean       NOT NULL DEFAULT false,
    send_sms_to           varchar(20)   NULL,

    travel_pnr_no         varchar(25)   NULL,
    seq_no_charges        integer       NULL,
    seq_no_acct_notes     integer       NULL,
    itinerary_no          integer       NULL,

    done_ind              boolean       NOT NULL DEFAULT false,
    done_by               varchar(10)   NULL,
    done_on               timestamptz   NULL,

    frz_ind               boolean       NOT NULL DEFAULT false,
    created_by            varchar(10)   NOT NULL,
    created_on            timestamptz   NOT NULL,
    updated_by            varchar(10)   NOT NULL,
    updated_on            timestamptz   NOT NULL,
    updated_at            varchar(20)   NOT NULL,

    CONSTRAINT pk_todo PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_todo_assigned_to_user ON sales97.todo (assigned_to_user_code, done_ind, frz_ind);
CREATE INDEX IF NOT EXISTS ix_todo_bkg_no           ON sales97.todo (bkg_no)              WHERE bkg_no IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_todo_quote_no         ON sales97.todo (quote_no)            WHERE quote_no IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_todo_tour_series      ON sales97.todo (tour_series_code, dep_date) WHERE tour_series_code IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_todo_client           ON sales97.todo (account_code_client) WHERE account_code_client IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_todo_supplier         ON sales97.todo (supplier_code)       WHERE supplier_code IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_todo_travel_pnr       ON sales97.todo (travel_pnr_no)       WHERE travel_pnr_no IS NOT NULL;
