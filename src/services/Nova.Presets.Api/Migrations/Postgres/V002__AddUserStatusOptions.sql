-- ============================================================
-- Nova.Presets.Api — V002
-- Postgres dialect
-- ============================================================

-- ------------------------------------------------------------
-- user_status_options
-- Scoping: XXXX/XXXX = tenant-wide, co/XXXX = all branches,
-- co/br = branch-specific. Most specific tier wins on read.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.user_status_options
(
    id           SERIAL        NOT NULL,
    tenant_id    varchar(10)   NOT NULL,
    company_code varchar(10)   NOT NULL,
    branch_code  varchar(10)   NOT NULL,
    status_code  varchar(50)   NOT NULL,
    label        varchar(200)  NOT NULL,
    colour       varchar(20)   NOT NULL,
    serial_no    int           NOT NULL DEFAULT 0,
    frz_ind      boolean       NOT NULL DEFAULT false,
    created_by   varchar(10)   NOT NULL,
    created_on   timestamptz   NOT NULL,
    updated_by   varchar(10)   NOT NULL,
    updated_on   timestamptz   NOT NULL,
    updated_at   varchar(50)   NOT NULL,
    CONSTRAINT pk_user_status_options PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_uso_tenant_company_branch
    ON presets.user_status_options (tenant_id, company_code, branch_code)
    WHERE frz_ind = false;
