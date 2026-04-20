-- ============================================================
-- Nova.CommonUX.Api — V005
-- MSSQL dialect
-- Adds user_security_rights and user_security_role_flag to nova_auth.
-- user_security_rights: one row per user-role-scope assignment.
-- user_security_role_flag: reference table mapping flag positions to meanings per role_code.
-- company_code / branch_code = 'XXXX' means the assignment applies to all companies / branches.
-- ============================================================

IF OBJECT_ID('nova_auth.dbo.user_security_rights', 'U') IS NULL
BEGIN
    CREATE TABLE nova_auth.dbo.user_security_rights
    (
        id             uniqueidentifier NOT NULL,
        tenant_id      varchar(10)      NOT NULL,
        user_id        varchar(10)      NOT NULL,
        role_code      varchar(10)      NOT NULL,
        role_flags     nvarchar(16)     NOT NULL DEFAULT '',
        company_code   varchar(10)      NOT NULL DEFAULT 'XXXX',
        branch_code    varchar(10)      NOT NULL DEFAULT 'XXXX',
        frz_ind        bit              NOT NULL DEFAULT 0,
        created_by     varchar(10)      NOT NULL,
        created_on     datetime2        NOT NULL,
        updated_by     varchar(10)      NOT NULL,
        updated_on     datetime2        NOT NULL,
        updated_at     varchar(50)      NOT NULL,
        CONSTRAINT pk_user_security_rights  PRIMARY KEY (id),
        CONSTRAINT uq_user_security_rights  UNIQUE (tenant_id, user_id, role_code, company_code, branch_code)
    );
END
GO

IF OBJECT_ID('nova_auth.dbo.user_security_role_flag', 'U') IS NULL
BEGIN
    CREATE TABLE nova_auth.dbo.user_security_role_flag
    (
        role_code      varchar(10)      NOT NULL,
        flag_position  int              NOT NULL,
        flag_name      varchar(50)      NOT NULL,
        flag_notes     varchar(200)     NULL,
        CONSTRAINT pk_user_security_role_flag PRIMARY KEY (role_code, flag_position)
    );
END
GO
