-- ============================================================
-- Nova.Presets.Api — V004
-- MSSQL dialect
-- Adds: group_task_templates, tour_generics, tenant_user_permissions
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'group_task_templates' AND schema_id = SCHEMA_ID('presets'))
BEGIN
    CREATE TABLE presets.group_task_templates
    (
        id                          uniqueidentifier  NOT NULL,
        tenant_id                   varchar(10)       NOT NULL,
        code                        varchar(10)       NOT NULL,
        name                        varchar(200)      NOT NULL,
        required                    bit               NOT NULL DEFAULT 0,
        critical                    bit               NOT NULL DEFAULT 0,
        group_task_sla_offset_days  int                   NULL,
        reference_date              varchar(20)       NOT NULL DEFAULT 'departure',
        source                      varchar(10)       NOT NULL DEFAULT 'GLOBAL',
        sort_order                  int                   NULL,
        frz_ind                     bit               NOT NULL DEFAULT 0,
        created_by                  varchar(10)       NOT NULL,
        created_on                  datetime2         NOT NULL,
        updated_by                  varchar(10)       NOT NULL,
        updated_on                  datetime2         NOT NULL,
        updated_at                  varchar(50)       NOT NULL,
        CONSTRAINT pk_group_task_templates PRIMARY KEY (id),
        CONSTRAINT uq_group_task_templates_code UNIQUE (tenant_id, code)
    );

    CREATE INDEX ix_group_task_templates_tenant
        ON presets.group_task_templates (tenant_id, sort_order, code);
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tour_generics' AND schema_id = SCHEMA_ID('presets'))
BEGIN
    CREATE TABLE presets.tour_generics
    (
        id          uniqueidentifier  NOT NULL,
        tenant_id   varchar(10)       NOT NULL,
        code        varchar(10)       NOT NULL,
        name        varchar(200)      NOT NULL,
        frz_ind     bit               NOT NULL DEFAULT 0,
        created_by  varchar(10)       NOT NULL,
        created_on  datetime2         NOT NULL,
        updated_by  varchar(10)       NOT NULL,
        updated_on  datetime2         NOT NULL,
        updated_at  varchar(50)       NOT NULL,
        CONSTRAINT pk_tour_generics PRIMARY KEY (id),
        CONSTRAINT uq_tour_generics_code UNIQUE (tenant_id, code)
    );

    CREATE INDEX ix_tour_generics_tenant
        ON presets.tour_generics (tenant_id)
        WHERE frz_ind = 0;
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tenant_user_permissions' AND schema_id = SCHEMA_ID('presets'))
BEGIN
    CREATE TABLE presets.tenant_user_permissions
    (
        tenant_id       varchar(10)     NOT NULL,
        user_id         varchar(10)     NOT NULL,
        permission_code varchar(100)    NOT NULL,
        CONSTRAINT pk_tenant_user_permissions PRIMARY KEY (tenant_id, user_id, permission_code)
    );

    CREATE INDEX ix_tenant_user_permissions_user
        ON presets.tenant_user_permissions (tenant_id, user_id);
END;
