-- ============================================================
-- Nova.CommonUX.Api — nova_auth schema
-- Postgres dialect
-- Connect to the target application database first, then run.
-- nova_auth is a schema (not a separate database).
-- ============================================================

CREATE SCHEMA IF NOT EXISTS nova_auth;

-- ------------------------------------------------------------
-- tenant_secrets
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS nova_auth.tenant_secrets
(
    tenant_id           varchar(10)     NOT NULL,
    client_secret_hash  varchar(500)    NOT NULL,
    frz_ind             boolean         NOT NULL DEFAULT false,
    created_by          varchar(10)     NOT NULL,
    created_on          timestamptz     NOT NULL,
    updated_by          varchar(10)     NOT NULL,
    updated_on          timestamptz     NOT NULL,
    updated_at          varchar(50)    NOT NULL,
    CONSTRAINT pk_tenant_secrets PRIMARY KEY (tenant_id)
);

-- ------------------------------------------------------------
-- tenant_user_auth
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS nova_auth.tenant_user_auth
(
    tenant_id               varchar(10)     NOT NULL,
    user_id                 varchar(10)     NOT NULL,
    password_hash           varchar(500)    NULL,
    totp_enabled            boolean         NOT NULL DEFAULT false,
    totp_secret_encrypted   varchar(500)    NULL,
    failed_login_count      integer         NOT NULL DEFAULT 0,
    locked_until            timestamptz     NULL,
    last_login_on           timestamptz     NULL,
    frz_ind                 boolean         NOT NULL DEFAULT false,
    created_by              varchar(10)     NOT NULL,
    created_on              timestamptz     NOT NULL,
    updated_by              varchar(10)     NOT NULL,
    updated_on              timestamptz     NOT NULL,
    updated_at              varchar(50)    NOT NULL,
    CONSTRAINT pk_tenant_user_auth PRIMARY KEY (tenant_id, user_id)
);

-- ------------------------------------------------------------
-- tenant_user_profile
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS nova_auth.tenant_user_profile
(
    tenant_id       varchar(10)     NOT NULL,
    user_id         varchar(10)     NOT NULL,
    email           varchar(255)    NOT NULL,
    display_name    varchar(200)    NOT NULL,
    avatar_url      varchar(500)    NULL,
    frz_ind         boolean         NOT NULL DEFAULT false,
    created_by      varchar(10)     NOT NULL,
    created_on      timestamptz     NOT NULL,
    updated_by      varchar(10)     NOT NULL,
    updated_on      timestamptz     NOT NULL,
    updated_at      varchar(50)    NOT NULL,
    CONSTRAINT pk_tenant_user_profile PRIMARY KEY (tenant_id, user_id)
);

-- ------------------------------------------------------------
-- tenant_user_social_identity
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS nova_auth.tenant_user_social_identity
(
    tenant_id           varchar(10)     NOT NULL,
    user_id             varchar(10)     NOT NULL,
    provider            varchar(50)     NOT NULL,
    provider_user_id    varchar(255)    NULL,
    provider_email      varchar(255)    NOT NULL,
    linked_on           timestamptz     NULL,
    frz_ind             boolean         NOT NULL DEFAULT false,
    created_by          varchar(10)     NOT NULL,
    created_on          timestamptz     NOT NULL,
    updated_by          varchar(10)     NOT NULL,
    updated_on          timestamptz     NOT NULL,
    updated_at          varchar(50)    NOT NULL,
    CONSTRAINT pk_tenant_user_social_identity PRIMARY KEY (tenant_id, user_id, provider)
);

-- ------------------------------------------------------------
-- tenant_auth_tokens
-- Immutable once written — no updated_* audit columns.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS nova_auth.tenant_auth_tokens
(
    id          uuid            NOT NULL,
    tenant_id   varchar(10)     NOT NULL,
    user_id     varchar(10)     NOT NULL,
    token_hash  varchar(500)    NOT NULL,
    token_type  varchar(50)     NOT NULL,
    expires_on  timestamptz     NOT NULL,
    used_on     timestamptz     NULL,
    created_on  timestamptz     NOT NULL,
    CONSTRAINT pk_tenant_auth_tokens PRIMARY KEY (id)
);

-- ------------------------------------------------------------
-- tenant_config
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS nova_auth.tenant_config
(
    tenant_id                       varchar(10)     NOT NULL,
    company_code                      varchar(10)     NOT NULL,
    branch_code                       varchar(10)     NOT NULL,
    tenant_name                     varchar(50)    NOT NULL,
    company_name                    varchar(50)    NOT NULL,
    branch_name                     varchar(50)    NOT NULL,
    client_name                     varchar(200)    NOT NULL,
    client_logo_url                 varchar(500)    NULL,
    active_users_inline_threshold   integer         NOT NULL DEFAULT 20,
    unclosed_web_enquiries_url      varchar(500)    NULL,
    task_list_url                   varchar(500)    NULL,
    breadcrumb_position             varchar(50)     NOT NULL DEFAULT 'inline',
    footer_gradient_refresh_ms      integer         NOT NULL DEFAULT 300000,
    frz_ind                         boolean         NOT NULL DEFAULT false,
    created_by                      varchar(10)     NOT NULL,
    created_on                      timestamptz     NOT NULL,
    updated_by                      varchar(10)     NOT NULL,
    updated_on                      timestamptz     NOT NULL,
    updated_at                      varchar(50)    NOT NULL,
    CONSTRAINT pk_tenant_config PRIMARY KEY (tenant_id, company_code, branch_code)
);

-- ------------------------------------------------------------
-- tenant_menu_items
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS nova_auth.tenant_menu_items
(
    menu_item_id            varchar(10)     NOT NULL,
    tenant_id               varchar(10)     NOT NULL,
    parent_id               varchar(10)     NULL,
    label                   varchar(200)    NOT NULL,
    icon                    varchar(100)    NULL,
    route                   varchar(500)    NULL,
    external_url_template   varchar(500)    NULL,
    external_url_param_mode varchar(20)     NOT NULL DEFAULT 'none',
    required_roles          varchar(500)    NULL,
    sort_order              integer         NOT NULL DEFAULT 0,
    is_active               boolean         NOT NULL DEFAULT true,
    frz_ind                 boolean         NOT NULL DEFAULT false,
    created_by              varchar(10)     NOT NULL,
    created_on              timestamptz     NOT NULL,
    updated_by              varchar(10)     NOT NULL,
    updated_on              timestamptz     NOT NULL,
    updated_at              varchar(50)    NOT NULL,
    CONSTRAINT pk_tenant_menu_items PRIMARY KEY (menu_item_id, tenant_id)
);

-- ------------------------------------------------------------
-- Indexes
-- ------------------------------------------------------------

CREATE UNIQUE INDEX IF NOT EXISTS ix_tenant_user_profile_email
    ON nova_auth.tenant_user_profile (tenant_id, email)
    WHERE frz_ind = false;

CREATE INDEX IF NOT EXISTS ix_tenant_user_social_lookup
    ON nova_auth.tenant_user_social_identity (tenant_id, provider, provider_user_id);

CREATE INDEX IF NOT EXISTS ix_tenant_user_social_pending
    ON nova_auth.tenant_user_social_identity (tenant_id, provider, provider_email)
    WHERE provider_user_id IS NULL;

CREATE INDEX IF NOT EXISTS ix_tenant_auth_tokens_hash
    ON nova_auth.tenant_auth_tokens (token_hash, token_type)
    WHERE used_on IS NULL;

CREATE INDEX IF NOT EXISTS ix_tenant_menu_items_tenant
    ON nova_auth.tenant_menu_items (tenant_id, sort_order)
    WHERE is_active = true AND frz_ind = false;
