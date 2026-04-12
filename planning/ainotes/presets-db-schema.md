# Nova.Presets.Api — Database Schema

**Database:** `presets`  
**Owner:** Nova.Presets.Api (sole writer for Nova tables — legacy tables may be written by legacy systems)  
**Dialect scripts:** MSSQL · Postgres · MariaDB

---

## Type Mapping Reference

| Concept | MSSQL | Postgres | MariaDB |
|---|---|---|---|
| Short string | `varchar(n)` / `nvarchar(n)` | `varchar(n)` | `VARCHAR(n)` |
| Boolean / flag | `bit` | `boolean` | `TINYINT(1)` |
| Timestamp | `datetime2` | `timestamptz` | `DATETIME` |
| UUID / GUID | `uniqueidentifier` | `uuid` | `CHAR(36)` |
| Integer | `int` | `integer` | `INT` |
| Boolean default false | `DEFAULT 0` | `DEFAULT false` | `DEFAULT 0` |

---

## Notes

- **Two connections:** Presets.Api uses **AuthDb** (read-only, `nova_auth` database) and **PresetsDb** (read-write, `presets` database). This schema document covers PresetsDb only.
- **Legacy tables** (`Company`, `Branch`) use PascalCase column names in MSSQL — they pre-date the Nova naming convention. Postgres and MariaDB versions use snake_case.
- **Legacy MSSQL `FrzInd`** may be `NULL` in existing data — application uses `ISNULL(FrzInd, 0) = 0` to filter active rows.
- **Audit columns** (`created_by`, `created_on`, `updated_by`, `updated_on`, `updated_at`) are set by the application layer, not DB defaults.
- **`updated_at`** stores the process name or IP address of the last writer.
- **Soft delete** uses `frz_ind` / `FrzInd` — `0 / false` = active, `1 / true` = deleted.
- **Foreign keys** are documented as logical relationships only — not enforced as DB constraints.
- **Postgres** — `presets` is a **schema** within the application database.
- **MariaDB** — `presets` is a **database**. All identifiers use backtick quoting.
- **Partial / filtered indexes** — supported in MSSQL and Postgres. MariaDB does not support them; note added where behaviour differs.
- **`tenant_password_change_requests`** has no `updated_*` audit columns — rows are immutable once written.

---

## Tables

1. `Company` — legacy company records (MSSQL PascalCase; `company` in Postgres/MariaDB)
2. `Branch` — legacy branch records (MSSQL PascalCase; `branch` in Postgres/MariaDB)
3. `tenant_user_status` — current presence status per user
4. `tenant_password_change_requests` — pending password change confirmations

---

## MSSQL

```sql
-- ============================================================
-- Nova.Presets.Api — presets database
-- MSSQL dialect
--
-- Legacy tables (Company, Branch) are guarded with IF OBJECT_ID IS NULL
-- so this script is safe to run against an existing presets DB — it
-- will no-op for any tables that already exist.
-- ============================================================

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'presets')
    CREATE DATABASE presets;
GO

-- ------------------------------------------------------------
-- Company (legacy table — no-op if already exists)
-- PascalCase column names — pre-Nova convention.
-- ------------------------------------------------------------
IF OBJECT_ID('presets.dbo.Company', 'U') IS NULL
BEGIN
    CREATE TABLE presets.dbo.Company
    (
        CompanyCode nvarchar(4)   NOT NULL,
        TenantId    nvarchar(10)  NOT NULL,
        CompanyName nvarchar(50)  NOT NULL,
        FrzInd      bit               NULL,
        CONSTRAINT pk_company PRIMARY KEY (CompanyCode)
    );
END
GO

-- ------------------------------------------------------------
-- Branch (legacy table — no-op if already exists)
-- PascalCase column names — pre-Nova convention.
-- ------------------------------------------------------------
IF OBJECT_ID('presets.dbo.Branch', 'U') IS NULL
BEGIN
    CREATE TABLE presets.dbo.Branch
    (
        BranchCode  nvarchar(4)   NOT NULL,
        CompanyCode nvarchar(4)   NOT NULL,
        BranchName  nvarchar(50)  NOT NULL,
        FrzInd      bit               NULL,
        CONSTRAINT pk_branch PRIMARY KEY (BranchCode, CompanyCode)
    );
END
GO

-- ------------------------------------------------------------
-- tenant_user_status (new Nova table)
-- One row per (tenant_id, user_id). UPSERT on status change.
-- ------------------------------------------------------------
IF OBJECT_ID('presets.dbo.tenant_user_status', 'U') IS NULL
BEGIN
    CREATE TABLE presets.dbo.tenant_user_status
    (
        tenant_id    varchar(10)   NOT NULL,
        user_id      varchar(10)   NOT NULL,
        status_id    varchar(50)   NOT NULL,
        status_label varchar(200)  NOT NULL,
        status_note  varchar(200)      NULL,
        frz_ind      bit           NOT NULL DEFAULT 0,
        created_by   varchar(10)   NOT NULL,
        created_on   datetime2     NOT NULL,
        updated_by   varchar(10)   NOT NULL,
        updated_on   datetime2     NOT NULL,
        updated_at   varchar(50)  NOT NULL,
        CONSTRAINT pk_tenant_user_status PRIMARY KEY (tenant_id, user_id)
    );
END
GO

-- ------------------------------------------------------------
-- tenant_password_change_requests (new Nova table)
-- Immutable once written. confirmed_on set once on confirmation.
-- ------------------------------------------------------------
IF OBJECT_ID('presets.dbo.tenant_password_change_requests', 'U') IS NULL
BEGIN
    CREATE TABLE presets.dbo.tenant_password_change_requests
    (
        id                 uniqueidentifier NOT NULL,
        tenant_id          varchar(10)      NOT NULL,
        user_id            varchar(10)      NOT NULL,
        new_password_hash  varchar(500)     NOT NULL,
        token_hash         varchar(500)     NOT NULL,
        expires_on         datetime2        NOT NULL,
        confirmed_on       datetime2            NULL,
        created_on         datetime2        NOT NULL,
        CONSTRAINT pk_tenant_password_change_requests PRIMARY KEY (id)
    );
END
GO

-- ------------------------------------------------------------
-- Indexes
-- ------------------------------------------------------------

-- Token confirmation lookup (unconfirmed tokens only)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_presets_pcr_token'
               AND object_id = OBJECT_ID('presets.dbo.tenant_password_change_requests'))
    CREATE INDEX ix_presets_pcr_token
        ON presets.dbo.tenant_password_change_requests (token_hash)
        WHERE confirmed_on IS NULL;
GO

-- Cleanup of old requests per user
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_presets_pcr_user'
               AND object_id = OBJECT_ID('presets.dbo.tenant_password_change_requests'))
    CREATE INDEX ix_presets_pcr_user
        ON presets.dbo.tenant_password_change_requests (tenant_id, user_id);
GO
```

---

## Postgres

```sql
-- ============================================================
-- Nova.Presets.Api — presets schema
-- Postgres dialect
-- Connect to the target application database first, then run.
-- presets is a schema (not a separate database).
-- ============================================================

CREATE SCHEMA IF NOT EXISTS presets;

-- ------------------------------------------------------------
-- company
-- snake_case — Nova convention applies for Postgres.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.company
(
    company_code varchar(4)   NOT NULL,
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
    branch_code  varchar(4)   NOT NULL,
    company_code varchar(4)   NOT NULL,
    branch_name  varchar(50)  NOT NULL,
    frz_ind      boolean      NOT NULL DEFAULT false,
    CONSTRAINT pk_branch PRIMARY KEY (branch_code, company_code)
);

-- ------------------------------------------------------------
-- tenant_user_status
-- One row per (tenant_id, user_id). UPSERT on status change.
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
-- Immutable once written. confirmed_on set once on confirmation.
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

-- Token confirmation lookup (unconfirmed tokens only)
CREATE INDEX IF NOT EXISTS ix_presets_pcr_token
    ON presets.tenant_password_change_requests (token_hash)
    WHERE confirmed_on IS NULL;

-- Cleanup of old requests per user
CREATE INDEX IF NOT EXISTS ix_presets_pcr_user
    ON presets.tenant_password_change_requests (tenant_id, user_id);

-- Company lookup by tenant
CREATE INDEX IF NOT EXISTS ix_presets_company_tenant
    ON presets.company (tenant_id);
```

---

## MariaDB

```sql
-- ============================================================
-- Nova.Presets.Api — presets database
-- MariaDB dialect
-- presets is a database. All identifiers use backtick quoting.
-- Note: MariaDB does not support partial/filtered indexes.
-- ix_presets_pcr_token covers all rows — application filters
-- confirmed_on IS NULL in the WHERE clause.
-- ============================================================

CREATE DATABASE IF NOT EXISTS `presets`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

-- ------------------------------------------------------------
-- company
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`company`
(
    `company_code` VARCHAR(4)   NOT NULL,
    `tenant_id`    VARCHAR(10)  NOT NULL,
    `company_name` VARCHAR(50)  NOT NULL,
    `frz_ind`      TINYINT(1)   NOT NULL DEFAULT 0,
    PRIMARY KEY (`company_code`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- branch
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`branch`
(
    `branch_code`  VARCHAR(4)   NOT NULL,
    `company_code` VARCHAR(4)   NOT NULL,
    `branch_name`  VARCHAR(50)  NOT NULL,
    `frz_ind`      TINYINT(1)   NOT NULL DEFAULT 0,
    PRIMARY KEY (`branch_code`, `company_code`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_user_status
-- One row per (tenant_id, user_id). UPSERT on status change.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`tenant_user_status`
(
    `tenant_id`    VARCHAR(10)   NOT NULL,
    `user_id`      VARCHAR(10)   NOT NULL,
    `status_id`    VARCHAR(50)   NOT NULL,
    `status_label` VARCHAR(200)  NOT NULL,
    `status_note`  VARCHAR(200)      NULL,
    `frz_ind`      TINYINT(1)    NOT NULL DEFAULT 0,
    `created_by`   VARCHAR(10)   NOT NULL,
    `created_on`   DATETIME      NOT NULL,
    `updated_by`   VARCHAR(10)   NOT NULL,
    `updated_on`   DATETIME      NOT NULL,
    `updated_at`   VARCHAR(50)  NOT NULL,
    PRIMARY KEY (`tenant_id`, `user_id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- tenant_password_change_requests
-- Immutable once written. confirmed_on set once on confirmation.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `presets`.`tenant_password_change_requests`
(
    `id`                 CHAR(36)      NOT NULL,
    `tenant_id`          VARCHAR(10)   NOT NULL,
    `user_id`            VARCHAR(10)   NOT NULL,
    `new_password_hash`  VARCHAR(500)  NOT NULL,
    `token_hash`         VARCHAR(500)  NOT NULL,
    `expires_on`         DATETIME      NOT NULL,
    `confirmed_on`       DATETIME          NULL,
    `created_on`         DATETIME      NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ------------------------------------------------------------
-- Indexes
-- Note: MariaDB does not support partial/filtered indexes.
-- ix_presets_pcr_token covers all rows — application filters
-- confirmed_on IS NULL in the WHERE clause.
-- ------------------------------------------------------------

CREATE INDEX `ix_presets_pcr_token`
    ON `presets`.`tenant_password_change_requests` (`token_hash`);

CREATE INDEX `ix_presets_pcr_user`
    ON `presets`.`tenant_password_change_requests` (`tenant_id`, `user_id`);

CREATE INDEX `ix_presets_company_tenant`
    ON `presets`.`company` (`tenant_id`);
```
