-- ============================================================
-- Nova.Presets.Api — V004
-- Postgres dialect
-- Adds: group_tasks, tour_generics, tenant_user_permissions
-- ============================================================

-- ------------------------------------------------------------
-- group_tasks
-- Tenant-scoped task template definitions.
-- sort_order: manual ordering (NULLS LAST on read).
-- frz_ind: soft-delete — excluded from normal reads.
-- source: GLOBAL | TG | TS | TD | CUSTOM
-- reference_date: departure | return | ji_exists
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.group_tasks
(
    id                          uuid            NOT NULL,
    tenant_id                   varchar(10)     NOT NULL,
    code                        varchar(10)     NOT NULL,
    name                        varchar(200)    NOT NULL,
    required                    boolean         NOT NULL DEFAULT false,
    critical                    boolean         NOT NULL DEFAULT false,
    group_task_sla_offset_days  int                 NULL,
    reference_date              varchar(20)     NOT NULL DEFAULT 'departure',
    source                      varchar(10)     NOT NULL DEFAULT 'GLOBAL',
    sort_order                  int                 NULL,
    frz_ind                     boolean         NOT NULL DEFAULT false,
    created_by                  varchar(10)     NOT NULL,
    created_on                  timestamptz     NOT NULL,
    updated_by                  varchar(10)     NOT NULL,
    updated_on                  timestamptz     NOT NULL,
    updated_at                  varchar(50)     NOT NULL,
    CONSTRAINT pk_group_tasks PRIMARY KEY (id),
    CONSTRAINT uq_group_tasks_code UNIQUE (tenant_id, code)
);

CREATE INDEX IF NOT EXISTS ix_group_tasks_tenant
    ON presets.group_tasks (tenant_id, sort_order NULLS LAST, code);

-- ------------------------------------------------------------
-- tour_generics
-- Tenant-scoped Tour Generic catalogue.
-- Codes are short uppercase identifiers (e.g. BHU, NEP).
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.tour_generics
(
    id            uuid            NOT NULL,
    tenant_id     varchar(10)     NOT NULL,
    code          varchar(10)     NOT NULL,
    name          varchar(200)    NOT NULL,
    company_code  varchar(4)      NOT NULL,
    branch_code   varchar(4)      NOT NULL,
    frz_ind       boolean         NOT NULL DEFAULT false,
    created_by    varchar(10)     NOT NULL,
    created_on    timestamptz     NOT NULL,
    updated_by    varchar(10)     NOT NULL,
    updated_on    timestamptz     NOT NULL,
    updated_at    varchar(50)     NOT NULL,
    CONSTRAINT pk_tour_generics PRIMARY KEY (id),
    CONSTRAINT uq_tour_generics_code UNIQUE (tenant_id, code)
);

CREATE INDEX IF NOT EXISTS ix_tour_generics_tenant
    ON presets.tour_generics (tenant_id)
    WHERE frz_ind = false;

-- ------------------------------------------------------------
-- tenant_user_permissions
-- Per-user permission flags (UI affordance hints; server also enforces).
-- permission_code examples: group_task_sla_edit, group_task_global_sla_edit
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.tenant_user_permissions
(
    tenant_id       varchar(10)     NOT NULL,
    user_id         varchar(10)     NOT NULL,
    permission_code varchar(100)    NOT NULL,
    CONSTRAINT pk_tenant_user_permissions PRIMARY KEY (tenant_id, user_id, permission_code)
);

CREATE INDEX IF NOT EXISTS ix_tenant_user_permissions_user
    ON presets.tenant_user_permissions (tenant_id, user_id);
