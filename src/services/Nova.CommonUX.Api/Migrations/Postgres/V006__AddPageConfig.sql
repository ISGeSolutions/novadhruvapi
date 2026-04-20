-- ============================================================
-- Nova.CommonUX.Api — V006
-- Postgres dialect
-- Adds page_config table to nova_auth schema.
-- Stores per-program UX configuration as JSON text.
-- ============================================================

CREATE TABLE IF NOT EXISTS nova_auth.page_config
(
    program_code  varchar(100)  NOT NULL,
    config_json   text              NULL,
    updated_by    varchar(10)   NOT NULL DEFAULT '',
    updated_on    timestamptz   NOT NULL,
    CONSTRAINT pk_page_config PRIMARY KEY (program_code)
);
