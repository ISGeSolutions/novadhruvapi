-- ============================================================
-- Nova.CommonUX.Api — V005
-- Postgres dialect
-- Adds user_security_rights and user_security_role_flag to nova_auth schema.
-- company_code / branch_code = 'XXXX' means the assignment applies to all companies / branches.
-- ============================================================

CREATE TABLE IF NOT EXISTS nova_auth.user_security_rights
(
    id             uuid         NOT NULL,
    tenant_id      varchar(10)  NOT NULL,
    user_id        varchar(10)  NOT NULL,
    role_code      varchar(10)  NOT NULL,
    role_flags     varchar(16)  NOT NULL DEFAULT '',
    company_code   varchar(10)  NOT NULL DEFAULT 'XXXX',
    branch_code    varchar(10)  NOT NULL DEFAULT 'XXXX',
    frz_ind        boolean      NOT NULL DEFAULT false,
    created_by     varchar(10)  NOT NULL,
    created_on     timestamptz  NOT NULL,
    updated_by     varchar(10)  NOT NULL,
    updated_on     timestamptz  NOT NULL,
    updated_at     varchar(50)  NOT NULL,
    CONSTRAINT pk_user_security_rights PRIMARY KEY (id),
    CONSTRAINT uq_user_security_rights UNIQUE (tenant_id, user_id, role_code, company_code, branch_code)
);

CREATE TABLE IF NOT EXISTS nova_auth.user_security_role_flag
(
    role_code      varchar(10)  NOT NULL,
    flag_position  integer      NOT NULL,
    flag_name      varchar(50)  NOT NULL,
    flag_notes     varchar(200) NULL,
    CONSTRAINT pk_user_security_role_flag PRIMARY KEY (role_code, flag_position)
);
