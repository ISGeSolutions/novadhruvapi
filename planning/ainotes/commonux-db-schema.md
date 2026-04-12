# Nova.CommonUX.Api — Database Schema

**Database:** `nova_auth`  
**Owner:** Nova.CommonUX.Api (sole writer — no other service writes to this database)  
**Dialect scripts:** MSSQL · Postgres · MariaDB

---

## Type Mapping Reference

| Concept | MSSQL | Postgres | MariaDB |
|---|---|---|---|
| Short string | `varchar(n)` | `varchar(n)` | `VARCHAR(n)` |
| Boolean / flag | `bit` | `boolean` | `TINYINT(1)` |
| Timestamp | `datetime2` | `timestamptz` | `DATETIME` |
| UUID / GUID | `uniqueidentifier` | `uuid` | `CHAR(36)` |
| Integer | `int` | `integer` | `INT` |
| Boolean default false | `DEFAULT 0` | `DEFAULT false` | `DEFAULT 0` |

---

## Notes

- **Audit columns** (`created_by`, `created_on`, `updated_by`, `updated_on`, `updated_at`) are set by the application layer, not DB defaults.
- **`updated_at`** stores the process name or IP address of the last writer — e.g. `Nova.CommonUX.Api` or `192.168.1.10`.
- **Soft delete** uses `frz_ind` throughout — project-wide convention. `0 / false` = active. `1 / true` = deleted.
- **Foreign keys** are documented as logical relationships only — not enforced as DB constraints (Dapper + multi-dialect pattern).
- **Postgres** — `nova_auth` is a **schema** within the application database. Run `CREATE SCHEMA` after connecting to the target database.
- **MariaDB** — `nova_auth` is a **database**. All identifiers use backtick quoting.
- **Partial / filtered indexes** — supported in MSSQL and Postgres. MariaDB does not support them; notes are added where behaviour differs.
- **`tenant_auth_tokens`** has no `updated_*` audit columns — tokens are immutable once written.

---

## Tables

1. `tenant_secrets` — M2M client secret hashes
2. `tenant_user_auth` — user credentials and auth state
3. `tenant_user_profile` — user identity and display data
4. `tenant_user_social_identity` — social provider links (Google / Microsoft / Apple)
5. `tenant_auth_tokens` — single-use tokens for password reset and magic link
6. `tenant_config` — UX/branding config and enabled auth methods per tenant/company/branch
7. `tenant_menu_items` — navigation menu tree per tenant

---

## MSSQL

```sql
-- ============================================================
-- Nova.CommonUX.Api — nova_auth database
-- MSSQL dialect
-- ============================================================

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'nova_auth')
    CREATE DATABASE nova_auth;
GO

-- ------------------------------------------------------------
-- tenant_secrets
-- One row per tenant. Stores Argon2id hash of the client_secret
-- used for machine-to-machine /auth/token calls.
-- ------------------------------------------------------------
IF OBJECT_ID('nova_auth.dbo.tenant_secrets', 'U') IS NULL
BEGIN
    CREATE TABLE nova_auth.dbo.tenant_secrets
    (
        tenant_id           varchar(10)     NOT NULL,
        client_secret_hash  varchar(500)    NOT NULL,
        frz_ind             bit             NOT NULL DEFAULT 0,
        created_by          varchar(10)     NOT NULL,
        created_on          datetime2       NOT NULL,
        updated_by          varchar(10)     NOT NULL,
        updated_on          datetime2       NOT NULL,
        updated_at          varchar(50)    NOT NULL,
        CONSTRAINT pk_tenant_secrets PRIMARY KEY (tenant_id)
    );
END
GO

-- ------------------------------------------------------------
-- tenant_user_auth
-- One row per (tenant_id, user_id). Stores credentials and
-- auth state. password_hash is nullable — social-only users
-- have no password.
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
        locked_until            datetime2       NULL,
        last_login_on           datetime2       NULL,
        frz_ind                 bit             NOT NULL DEFAULT 0,
        created_by              varchar(10)     NOT NULL,
        created_on              datetime2       NOT NULL,
        updated_by              varchar(10)     NOT NULL,
        updated_on              datetime2       NOT NULL,
        updated_at              varchar(50)    NOT NULL,
        CONSTRAINT pk_tenant_user_auth PRIMARY KEY (tenant_id, user_id)
    );
END
GO

-- ------------------------------------------------------------
-- tenant_user_profile
-- One row per (tenant_id, user_id). Stores identity and
-- display data returned in login responses.
-- email is unique per tenant (active rows only — filtered index).
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
        created_on      datetime2       NOT NULL,
        updated_by      varchar(10)     NOT NULL,
        updated_on      datetime2       NOT NULL,
        updated_at      varchar(50)    NOT NULL,
        CONSTRAINT pk_tenant_user_profile PRIMARY KEY (tenant_id, user_id)
    );
END
GO

-- ------------------------------------------------------------
-- tenant_user_social_identity
-- One row per (tenant_id, user_id, provider).
-- provider_user_id NULL = admin-provisioned pending link.
-- provider_user_id populated = fully resolved link.
-- ------------------------------------------------------------
IF OBJECT_ID('nova_auth.dbo.tenant_user_social_identity', 'U') IS NULL
BEGIN
    CREATE TABLE nova_auth.dbo.tenant_user_social_identity
    (
        tenant_id           varchar(10)     NOT NULL,
        user_id             varchar(10)     NOT NULL,
        provider            varchar(50)     NOT NULL,  -- google | microsoft | apple
        provider_user_id    varchar(255)    NULL,
        provider_email      varchar(255)    NOT NULL,
        linked_on           datetime2       NULL,
        frz_ind             bit             NOT NULL DEFAULT 0,
        created_by          varchar(10)     NOT NULL,
        created_on          datetime2       NOT NULL,
        updated_by          varchar(10)     NOT NULL,
        updated_on          datetime2       NOT NULL,
        updated_at          varchar(50)    NOT NULL,
        CONSTRAINT pk_tenant_user_social_identity PRIMARY KEY (tenant_id, user_id, provider)
    );
END
GO

-- ------------------------------------------------------------
-- tenant_auth_tokens
-- Single-use tokens for password_reset and magic_link flows.
-- Stores SHA-256 hash only — plaintext sent to user by email.
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
        token_type  varchar(50)         NOT NULL,  -- password_reset | magic_link
        expires_on  datetime2           NOT NULL,
        used_on     datetime2           NULL,
        created_on  datetime2           NOT NULL,
        CONSTRAINT pk_tenant_auth_tokens PRIMARY KEY (id)
    );
END
GO

-- ------------------------------------------------------------
-- tenant_config
-- One row per (tenant_id, company_id, branch_id).
-- UX and branding configuration per tenant/company/branch.
-- enabled_auth_methods added by V002__AddEnabledAuthMethods.sql
-- ------------------------------------------------------------
IF OBJECT_ID('nova_auth.dbo.tenant_config', 'U') IS NULL
BEGIN
    CREATE TABLE nova_auth.dbo.tenant_config
    (
        tenant_id                       varchar(10)     NOT NULL,
        company_id                      varchar(10)     NOT NULL,
        branch_id                       varchar(10)     NOT NULL,
        tenant_name                     varchar(200)    NOT NULL,
        company_name                    varchar(200)    NOT NULL,
        branch_name                     varchar(200)    NOT NULL,
        client_name                     varchar(200)    NOT NULL,
        client_logo_url                 varchar(500)    NULL,
        active_users_inline_threshold   int             NOT NULL DEFAULT 20,
        unclosed_web_enquiries_url      varchar(500)    NULL,
        task_list_url                   varchar(500)    NULL,
        breadcrumb_position             varchar(50)     NOT NULL DEFAULT 'inline',
        footer_gradient_refresh_ms      int             NOT NULL DEFAULT 300000,
        enabled_auth_methods            varchar(200)    NULL,
        frz_ind                         bit             NOT NULL DEFAULT 0,
        created_by                      varchar(10)     NOT NULL,
        created_on                      datetime2       NOT NULL,
        updated_by                      varchar(10)     NOT NULL,
        updated_on                      datetime2       NOT NULL,
        updated_at                      varchar(50)    NOT NULL,
        CONSTRAINT pk_tenant_config PRIMARY KEY (tenant_id, company_id, branch_id)
    );
END
GO

-- ------------------------------------------------------------
-- tenant_menu_items
-- Navigation menu items per tenant. Parent/child nesting via parent_id.
-- Role filtering via required_roles.
-- ------------------------------------------------------------
IF OBJECT_ID('nova_auth.dbo.tenant_menu_items', 'U') IS NULL
BEGIN
    CREATE TABLE nova_auth.dbo.tenant_menu_items
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
        sort_order              int             NOT NULL DEFAULT 0,
        is_active               bit             NOT NULL DEFAULT 1,
        frz_ind                 bit             NOT NULL DEFAULT 0,
        created_by              varchar(10)     NOT NULL,
        created_on              datetime2       NOT NULL,
        updated_by              varchar(10)     NOT NULL,
        updated_on              datetime2       NOT NULL,
        updated_at              varchar(50)    NOT NULL,
        CONSTRAINT pk_tenant_menu_items PRIMARY KEY (menu_item_id, tenant_id)
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

-- Menu items by tenant and sort order (active items only)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_tenant_menu_items_tenant'
               AND object_id = OBJECT_ID('nova_auth.dbo.tenant_menu_items'))
    CREATE INDEX ix_tenant_menu_items_tenant
        ON nova_auth.dbo.tenant_menu_items (tenant_id, sort_order)
        WHERE is_active = 1 AND frz_ind = 0;
GO
```

---

## Postgres

```sql
-- ============================================================
-- Nova.CommonUX.Api — nova_auth schema
-- Postgres dialect
-- Connect to the target application database first, then run.
-- nova_auth is a schema, not a separate database.
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
    provider            varchar(50)     NOT NULL,  -- google | microsoft | apple
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
    token_type  varchar(50)     NOT NULL,  -- password_reset | magic_link
    expires_on  timestamptz     NOT NULL,
    used_on     timestamptz     NULL,
    created_on  timestamptz     NOT NULL,
    CONSTRAINT pk_tenant_auth_tokens PRIMARY KEY (id)
);

-- ------------------------------------------------------------
-- tenant_config
-- One row per (tenant_id, company_id, branch_id).
-- UX and branding configuration per tenant/company/branch.
-- enabled_auth_methods added by V002__AddEnabledAuthMethods.sql
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS nova_auth.tenant_config
(
    tenant_id                       varchar(10)     NOT NULL,
    company_id                      varchar(10)     NOT NULL,
    branch_id                       varchar(10)     NOT NULL,
    tenant_name                     varchar(200)    NOT NULL,
    company_name                    varchar(200)    NOT NULL,
    branch_name                     varchar(200)    NOT NULL,
    client_name                     varchar(200)    NOT NULL,
    client_logo_url                 varchar(500)    NULL,
    active_users_inline_threshold   integer         NOT NULL DEFAULT 20,
    unclosed_web_enquiries_url      varchar(500)    NULL,
    task_list_url                   varchar(500)    NULL,
    breadcrumb_position             varchar(50)     NOT NULL DEFAULT 'inline',
    footer_gradient_refresh_ms      integer         NOT NULL DEFAULT 300000,
    enabled_auth_methods            varchar(200)    NULL,
    frz_ind                         boolean         NOT NULL DEFAULT false,
    created_by                      varchar(10)     NOT NULL,
    created_on                      timestamptz     NOT NULL,
    updated_by                      varchar(10)     NOT NULL,
    updated_on                      timestamptz     NOT NULL,
    updated_at                      varchar(50)    NOT NULL,
    CONSTRAINT pk_tenant_config PRIMARY KEY (tenant_id, company_id, branch_id)
);

-- ------------------------------------------------------------
-- tenant_menu_items
-- Navigation menu items per tenant. Parent/child nesting via parent_id.
-- Role filtering via required_roles.
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

-- Unique email per tenant, active rows only (partial index)
CREATE UNIQUE INDEX IF NOT EXISTS ix_tenant_user_profile_email
    ON nova_auth.tenant_user_profile (tenant_id, email)
    WHERE frz_ind = false;

-- Social login lookup by resolved provider_user_id
CREATE INDEX IF NOT EXISTS ix_tenant_user_social_lookup
    ON nova_auth.tenant_user_social_identity (tenant_id, provider, provider_user_id);

-- Social login lookup by provider_email for pending (admin-provisioned) links
CREATE INDEX IF NOT EXISTS ix_tenant_user_social_pending
    ON nova_auth.tenant_user_social_identity (tenant_id, provider, provider_email)
    WHERE provider_user_id IS NULL;

-- Token verification by hash (unused tokens only)
CREATE INDEX IF NOT EXISTS ix_tenant_auth_tokens_hash
    ON nova_auth.tenant_auth_tokens (token_hash, token_type)
    WHERE used_on IS NULL;

-- Menu items by tenant and sort order (active items only)
CREATE INDEX IF NOT EXISTS ix_tenant_menu_items_tenant
    ON nova_auth.tenant_menu_items (tenant_id, sort_order)
    WHERE is_active = true AND frz_ind = false;
```

---

## MariaDB

```sql
-- ============================================================
-- Nova.CommonUX.Api — nova_auth database
-- MariaDB dialect
-- nova_auth is a database. All identifiers use backtick quoting.
-- Note: MariaDB does not support partial/filtered indexes.
-- See index notes below.
-- ============================================================

CREATE DATABASE IF NOT EXISTS `nova_auth`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

-- ------------------------------------------------------------
-- tenant_secrets
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `nova_auth`.`tenant_secrets`
(
    `tenant_id`           VARCHAR(10)     NOT NULL,
    `client_secret_hash`  VARCHAR(500)    NOT NULL,
    `frz_ind`             TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`          VARCHAR(10)     NOT NULL,
    `created_on`          DATETIME        NOT NULL,
    `updated_by`          VARCHAR(10)     NOT NULL,
    `updated_on`          DATETIME        NOT NULL,
    `updated_at`          VARCHAR(50)    NOT NULL,
    PRIMARY KEY (`tenant_id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_user_auth
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `nova_auth`.`tenant_user_auth`
(
    `tenant_id`               VARCHAR(10)     NOT NULL,
    `user_id`                 VARCHAR(10)     NOT NULL,
    `password_hash`           VARCHAR(500)    NULL,
    `totp_enabled`            TINYINT(1)      NOT NULL DEFAULT 0,
    `totp_secret_encrypted`   VARCHAR(500)    NULL,
    `failed_login_count`      INT             NOT NULL DEFAULT 0,
    `locked_until`            DATETIME        NULL,
    `last_login_on`           DATETIME        NULL,
    `frz_ind`                 TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`              VARCHAR(10)     NOT NULL,
    `created_on`              DATETIME        NOT NULL,
    `updated_by`              VARCHAR(10)     NOT NULL,
    `updated_on`              DATETIME        NOT NULL,
    `updated_at`              VARCHAR(50)    NOT NULL,
    PRIMARY KEY (`tenant_id`, `user_id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_user_profile
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `nova_auth`.`tenant_user_profile`
(
    `tenant_id`       VARCHAR(10)     NOT NULL,
    `user_id`         VARCHAR(10)     NOT NULL,
    `email`           VARCHAR(255)    NOT NULL,
    `display_name`    VARCHAR(200)    NOT NULL,
    `avatar_url`      VARCHAR(500)    NULL,
    `frz_ind`         TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`      VARCHAR(10)     NOT NULL,
    `created_on`      DATETIME        NOT NULL,
    `updated_by`      VARCHAR(10)     NOT NULL,
    `updated_on`      DATETIME        NOT NULL,
    `updated_at`      VARCHAR(50)    NOT NULL,
    PRIMARY KEY (`tenant_id`, `user_id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_user_social_identity
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `nova_auth`.`tenant_user_social_identity`
(
    `tenant_id`           VARCHAR(10)     NOT NULL,
    `user_id`             VARCHAR(10)     NOT NULL,
    `provider`            VARCHAR(50)     NOT NULL,  -- google | microsoft | apple
    `provider_user_id`    VARCHAR(255)    NULL,
    `provider_email`      VARCHAR(255)    NOT NULL,
    `linked_on`           DATETIME        NULL,
    `frz_ind`             TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`          VARCHAR(10)     NOT NULL,
    `created_on`          DATETIME        NOT NULL,
    `updated_by`          VARCHAR(10)     NOT NULL,
    `updated_on`          DATETIME        NOT NULL,
    `updated_at`          VARCHAR(50)    NOT NULL,
    PRIMARY KEY (`tenant_id`, `user_id`, `provider`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_auth_tokens
-- Immutable once written — no updated_* audit columns.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `nova_auth`.`tenant_auth_tokens`
(
    `id`          CHAR(36)        NOT NULL,
    `tenant_id`   VARCHAR(10)     NOT NULL,
    `user_id`     VARCHAR(10)     NOT NULL,
    `token_hash`  VARCHAR(500)    NOT NULL,
    `token_type`  VARCHAR(50)     NOT NULL,  -- password_reset | magic_link
    `expires_on`  DATETIME        NOT NULL,
    `used_on`     DATETIME        NULL,
    `created_on`  DATETIME        NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_config
-- One row per (tenant_id, company_id, branch_id).
-- UX and branding configuration per tenant/company/branch.
-- enabled_auth_methods added by V002__AddEnabledAuthMethods.sql
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `nova_auth`.`tenant_config`
(
    `tenant_id`                       VARCHAR(10)     NOT NULL,
    `company_id`                      VARCHAR(10)     NOT NULL,
    `branch_id`                       VARCHAR(10)     NOT NULL,
    `tenant_name`                     VARCHAR(200)    NOT NULL,
    `company_name`                    VARCHAR(200)    NOT NULL,
    `branch_name`                     VARCHAR(200)    NOT NULL,
    `client_name`                     VARCHAR(200)    NOT NULL,
    `client_logo_url`                 VARCHAR(500)    NULL,
    `active_users_inline_threshold`   INT             NOT NULL DEFAULT 20,
    `unclosed_web_enquiries_url`      VARCHAR(500)    NULL,
    `task_list_url`                   VARCHAR(500)    NULL,
    `breadcrumb_position`             VARCHAR(50)     NOT NULL DEFAULT 'inline',
    `footer_gradient_refresh_ms`      INT             NOT NULL DEFAULT 300000,
    `enabled_auth_methods`            VARCHAR(200)    NULL,
    `frz_ind`                         TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`                      VARCHAR(10)     NOT NULL,
    `created_on`                      DATETIME        NOT NULL,
    `updated_by`                      VARCHAR(10)     NOT NULL,
    `updated_on`                      DATETIME        NOT NULL,
    `updated_at`                      VARCHAR(50)    NOT NULL,
    PRIMARY KEY (`tenant_id`, `company_id`, `branch_id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_menu_items
-- Navigation menu items per tenant. Parent/child nesting via parent_id.
-- Role filtering via required_roles.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `nova_auth`.`tenant_menu_items`
(
    `menu_item_id`            VARCHAR(10)     NOT NULL,
    `tenant_id`               VARCHAR(10)     NOT NULL,
    `parent_id`               VARCHAR(10)     NULL,
    `label`                   VARCHAR(200)    NOT NULL,
    `icon`                    VARCHAR(100)    NULL,
    `route`                   VARCHAR(500)    NULL,
    `external_url_template`   VARCHAR(500)    NULL,
    `external_url_param_mode` VARCHAR(20)     NOT NULL DEFAULT 'none',
    `required_roles`          VARCHAR(500)    NULL,
    `sort_order`              INT             NOT NULL DEFAULT 0,
    `is_active`               TINYINT(1)      NOT NULL DEFAULT 1,
    `frz_ind`                 TINYINT(1)      NOT NULL DEFAULT 0,
    `created_by`              VARCHAR(10)     NOT NULL,
    `created_on`              DATETIME        NOT NULL,
    `updated_by`              VARCHAR(10)     NOT NULL,
    `updated_on`              DATETIME        NOT NULL,
    `updated_at`              VARCHAR(50)    NOT NULL,
    PRIMARY KEY (`menu_item_id`, `tenant_id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- Indexes
-- Note: MariaDB does not support partial/filtered indexes.
-- ix_tenant_user_profile_email is a full unique index —
--   soft-deleted rows (frz_ind=1) will block email reuse.
--   Application must check frz_ind before raising a conflict.
-- ix_tenant_user_social_pending covers all rows regardless
--   of provider_user_id — the NULL filter is enforced in SQL.
-- ix_tenant_menu_items_tenant covers all rows — application
--   filters is_active = 1 AND frz_ind = 0 in the WHERE clause.
-- ------------------------------------------------------------

-- Unique email per tenant (all rows — no partial index support)
CREATE UNIQUE INDEX `ix_tenant_user_profile_email`
    ON `nova_auth`.`tenant_user_profile` (`tenant_id`, `email`);

-- Social login lookup by resolved provider_user_id
CREATE INDEX `ix_tenant_user_social_lookup`
    ON `nova_auth`.`tenant_user_social_identity` (`tenant_id`, `provider`, `provider_user_id`);

-- Social login lookup by provider_email (pending links)
CREATE INDEX `ix_tenant_user_social_pending`
    ON `nova_auth`.`tenant_user_social_identity` (`tenant_id`, `provider`, `provider_email`);

-- Token verification by hash
CREATE INDEX `ix_tenant_auth_tokens_hash`
    ON `nova_auth`.`tenant_auth_tokens` (`token_hash`, `token_type`);

-- Menu items by tenant and sort order
CREATE INDEX `ix_tenant_menu_items_tenant`
    ON `nova_auth`.`tenant_menu_items` (`tenant_id`, `sort_order`);
```

---

## V002 — enabled_auth_methods

Adds `enabled_auth_methods varchar(200) NULL` to `tenant_config`. Applied after V001. `NULL` = all four methods enabled (`google`, `microsoft`, `apple`, `magic_link`). Non-null values are stored comma-separated.

### MSSQL

```sql
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('nova_auth.dbo.tenant_config')
      AND name = 'enabled_auth_methods'
)
BEGIN
    ALTER TABLE nova_auth.dbo.tenant_config
        ADD enabled_auth_methods varchar(200) NULL;
END
GO
```

### Postgres

```sql
ALTER TABLE nova_auth.tenant_config
    ADD COLUMN IF NOT EXISTS enabled_auth_methods varchar(200) NULL;
```

### MariaDB

```sql
ALTER TABLE `nova_auth`.`tenant_config`
    ADD COLUMN IF NOT EXISTS `enabled_auth_methods` VARCHAR(200) NULL;
```
