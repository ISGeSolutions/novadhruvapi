-- ============================================================
-- Nova.Presets.Api — presets schema
-- Postgres dialect
-- Connect to the target application database first, then run.
-- presets is a schema (not a separate database).
-- ============================================================

CREATE SCHEMA IF NOT EXISTS presets;

-- ------------------------------------------------------------
-- company
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.company
(
    company_code varchar(10)   NOT NULL,
    tenant_id    varchar(10)  NOT NULL,
    company_name varchar(50)  NOT NULL,
    frz_ind      boolean      NOT NULL DEFAULT false,
    CONSTRAINT pk_company PRIMARY KEY (company_code)
);

-- ------------------------------------------------------------
-- branch
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.branch
(
    branch_code  varchar(10)   NOT NULL,
    company_code varchar(10)   NOT NULL,
    branch_name  varchar(50)  NOT NULL,
    frz_ind      boolean      NOT NULL DEFAULT false,
    CONSTRAINT pk_branch PRIMARY KEY (branch_code, company_code)
);

-- ------------------------------------------------------------
-- tenant_user_status
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.tenant_user_status
(
    tenant_id    varchar(10)   NOT NULL,
    user_id      varchar(10)   NOT NULL,
    status_id    varchar(50)   NOT NULL,
    status_label varchar(200)  NOT NULL,
    status_note  varchar(200)      NULL,
    frz_ind      boolean       NOT NULL DEFAULT false,
    created_by   varchar(10)   NOT NULL,
    created_on   timestamptz   NOT NULL,
    updated_by   varchar(10)   NOT NULL,
    updated_on   timestamptz   NOT NULL,
    updated_at   varchar(50)  NOT NULL,
    CONSTRAINT pk_tenant_user_status PRIMARY KEY (tenant_id, user_id)
);

-- ------------------------------------------------------------
-- tenant_password_change_requests
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.tenant_password_change_requests
(
    id                 uuid          NOT NULL,
    tenant_id          varchar(10)   NOT NULL,
    user_id            varchar(10)   NOT NULL,
    new_password_hash  varchar(500)  NOT NULL,
    token_hash         varchar(500)  NOT NULL,
    expires_on         timestamptz   NOT NULL,
    confirmed_on       timestamptz       NULL,
    created_on         timestamptz   NOT NULL,
    CONSTRAINT pk_tenant_password_change_requests PRIMARY KEY (id)
);

-- ------------------------------------------------------------
-- Indexes
-- ------------------------------------------------------------

CREATE INDEX IF NOT EXISTS ix_presets_pcr_token
    ON presets.tenant_password_change_requests (token_hash)
    WHERE confirmed_on IS NULL;

CREATE INDEX IF NOT EXISTS ix_presets_pcr_user
    ON presets.tenant_password_change_requests (tenant_id, user_id);

CREATE INDEX IF NOT EXISTS ix_presets_company_tenant
    ON presets.company (tenant_id);
