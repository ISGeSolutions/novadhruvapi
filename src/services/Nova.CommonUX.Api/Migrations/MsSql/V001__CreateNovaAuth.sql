-- ============================================================
-- Nova.CommonUX.Api — nova_auth database
-- MSSQL dialect
-- ============================================================

-- ------------------------------------------------------------
-- tenant_secrets
-- One row per tenant. Argon2id hash of the M2M client_secret.
-- ------------------------------------------------------------
IF OBJECT_ID('nova_auth.dbo.tenant_secrets', 'U') IS NULL
BEGIN
    CREATE TABLE nova_auth.dbo.tenant_secrets
    (
        tenant_id           varchar(10)     NOT NULL,
        client_secret_hash  varchar(500)    NOT NULL,
        frz_ind             bit             NOT NULL DEFAULT 0,
        created_by          varchar(10)     NOT NULL,
        created_on          datetimeoffset  NOT NULL,
        updated_by          varchar(10)     NOT NULL,
        updated_on          datetimeoffset  NOT NULL,
        updated_at          varchar(50)     NOT NULL,
        CONSTRAINT pk_tenant_secrets PRIMARY KEY (tenant_id)
    );
END
GO

-- ------------------------------------------------------------
-- tenant_user_auth
-- One row per (tenant_id, user_id). Credentials + auth state.
-- ------------------------------------------------------------
IF OBJECT_ID('nova_auth.dbo.tenant_user_auth', 'U') IS NULL
BEGIN
    CREATE TABLE nova_auth.dbo.tenant_user_auth
    (
        tenant_id               varchar(10)     NOT NULL,
        user_id                 varchar(10)     NOT NULL,
        password_hash           varchar(500)    NULL,
        totp_enabled            bit             NOT NULL DEFAULT 0,
        totp_secret_encrypted   varchar(500)    NULL,
        failed_login_count      int             NOT NULL DEFAULT 0,
        locked_until            datetimeoffset           NULL,
        last_login_on           datetimeoffset           NULL,
        frz_ind                 bit             NOT NULL DEFAULT 0,
        created_by              varchar(10)     NOT NULL,
        created_on              datetimeoffset           NOT NULL,
        updated_by              varchar(10)     NOT NULL,
        updated_on              datetimeoffset           NOT NULL,
        updated_at              varchar(50)    NOT NULL,
        CONSTRAINT pk_tenant_user_auth PRIMARY KEY (tenant_id, user_id)
    );
END
GO

-- ------------------------------------------------------------
-- tenant_user_profile
-- One row per (tenant_id, user_id). Identity and display data.
-- ------------------------------------------------------------
IF OBJECT_ID('nova_auth.dbo.tenant_user_profile', 'U') IS NULL
BEGIN
    CREATE TABLE nova_auth.dbo.tenant_user_profile
    (
        tenant_id       varchar(10)     NOT NULL,
        user_id         varchar(10)     NOT NULL,
        email           varchar(255)    NOT NULL,
        display_name    varchar(200)    NOT NULL,
        avatar_url      varchar(500)    NULL,
        frz_ind         bit             NOT NULL DEFAULT 0,
        created_by      varchar(10)     NOT NULL,
        created_on      datetimeoffset           NOT NULL,
        updated_by      varchar(10)     NOT NULL,
        updated_on      datetimeoffset           NOT NULL,
        updated_at      varchar(50)    NOT NULL,
        CONSTRAINT pk_tenant_user_profile PRIMARY KEY (tenant_id, user_id)
    );
END
GO

-- ------------------------------------------------------------
-- tenant_user_social_identity
-- One row per (tenant_id, user_id, provider).
-- provider_user_id NULL = admin-provisioned pending link.
-- ------------------------------------------------------------
IF OBJECT_ID('nova_auth.dbo.tenant_user_social_identity', 'U') IS NULL
BEGIN
    CREATE TABLE nova_auth.dbo.tenant_user_social_identity
    (
        tenant_id           varchar(10)     NOT NULL,
        user_id             varchar(10)     NOT NULL,
        provider            varchar(50)     NOT NULL,
        provider_user_id    varchar(255)    NULL,
        provider_email      varchar(255)    NOT NULL,
        linked_on           datetimeoffset           NULL,
        frz_ind             bit             NOT NULL DEFAULT 0,
        created_by          varchar(10)     NOT NULL,
        created_on          datetimeoffset           NOT NULL,
        updated_by          varchar(10)     NOT NULL,
        updated_on          datetimeoffset           NOT NULL,
        updated_at          varchar(50)    NOT NULL,
        CONSTRAINT pk_tenant_user_social_identity PRIMARY KEY (tenant_id, user_id, provider)
    );
END
GO

-- ------------------------------------------------------------
-- tenant_auth_tokens
-- Single-use tokens for password_reset and magic_link.
-- Immutable once written — no updated_* audit columns.
-- ------------------------------------------------------------
IF OBJECT_ID('nova_auth.dbo.tenant_auth_tokens', 'U') IS NULL
BEGIN
    CREATE TABLE nova_auth.dbo.tenant_auth_tokens
    (
        id          uniqueidentifier    NOT NULL,
        tenant_id   varchar(10)         NOT NULL,
        user_id     varchar(10)         NOT NULL,
        token_hash  varchar(500)        NOT NULL,
        token_type  varchar(50)         NOT NULL,
        expires_on  datetimeoffset    NOT NULL,
        used_on     datetimeoffset    NULL,
        created_on  datetimeoffset    NOT NULL,
        CONSTRAINT pk_tenant_auth_tokens PRIMARY KEY (id)
    );
END
GO

-- ------------------------------------------------------------
-- tenant_config
-- One row per (tenant_id, company_code, branch_code).
-- UX and branding configuration.
-- ------------------------------------------------------------
IF OBJECT_ID('nova_auth.dbo.tenant_config', 'U') IS NULL
BEGIN
    CREATE TABLE nova_auth.dbo.tenant_config
    (
        tenant_id                       varchar(10)     NOT NULL,
        company_code                    varchar(10)     NOT NULL,
        branch_code                     varchar(10)     NOT NULL,
        tenant_name                     varchar(50)    NOT NULL,
        company_name                    varchar(50)    NOT NULL,
        branch_name                     varchar(50)    NOT NULL,
        client_name                     varchar(50)    NOT NULL,
        client_logo_url                 varchar(500)    NULL,
        active_users_inline_threshold   int             NOT NULL DEFAULT 20,
        unclosed_web_enquiries_url      varchar(500)    NULL,
        task_list_url                   varchar(500)    NULL,
        breadcrumb_position             varchar(50)     NOT NULL DEFAULT 'inline',
        footer_gradient_refresh_ms      int             NOT NULL DEFAULT 300000,
        frz_ind                         bit             NOT NULL DEFAULT 0,
        created_by                      varchar(10)     NOT NULL,
        created_on                      datetimeoffset           NOT NULL,
        updated_by                      varchar(10)     NOT NULL,
        updated_on                      datetimeoffset           NOT NULL,
        updated_at                      varchar(50)    NOT NULL,
        CONSTRAINT pk_tenant_config PRIMARY KEY (tenant_id, company_code, branch_code)
    );
END
GO


-- ------------------------------------------------------------
-- Indexes
-- ------------------------------------------------------------

-- Unique email per tenant, active rows only (filtered index)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_tenant_user_profile_email'
               AND object_id = OBJECT_ID('nova_auth.dbo.tenant_user_profile'))
    CREATE UNIQUE INDEX ix_tenant_user_profile_email
        ON nova_auth.dbo.tenant_user_profile (tenant_id, email)
        WHERE frz_ind = 0;
GO

-- Social login lookup by resolved provider_user_id
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_tenant_user_social_lookup'
               AND object_id = OBJECT_ID('nova_auth.dbo.tenant_user_social_identity'))
    CREATE INDEX ix_tenant_user_social_lookup
        ON nova_auth.dbo.tenant_user_social_identity (tenant_id, provider, provider_user_id);
GO

-- Social login lookup by provider_email for pending (admin-provisioned) links
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_tenant_user_social_pending'
               AND object_id = OBJECT_ID('nova_auth.dbo.tenant_user_social_identity'))
    CREATE INDEX ix_tenant_user_social_pending
        ON nova_auth.dbo.tenant_user_social_identity (tenant_id, provider, provider_email)
        WHERE provider_user_id IS NULL;
GO

-- Token verification by hash (unused tokens only)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_tenant_auth_tokens_hash'
               AND object_id = OBJECT_ID('nova_auth.dbo.tenant_auth_tokens'))
    CREATE INDEX ix_tenant_auth_tokens_hash
        ON nova_auth.dbo.tenant_auth_tokens (token_hash, token_type)
        WHERE used_on IS NULL;
GO

