-- ============================================================
-- Nova.Presets.Api — presets database
-- MSSQL dialect
--
-- Legacy tables (Company, Branch) are guarded with IF OBJECT_ID IS NULL
-- so this script is safe to run against an existing presets DB — it
-- will no-op for any tables that already exist.
-- ============================================================

-- ------------------------------------------------------------
-- Company (legacy table — no-op if already exists)
-- ------------------------------------------------------------
IF OBJECT_ID('presets.dbo.Company', 'U') IS NULL
BEGIN
    CREATE TABLE presets.dbo.Company
    (
        CompanyCode nvarchar(10)   NOT NULL,
        TenantId    nvarchar(10)  NOT NULL,
        CompanyName nvarchar(50)  NOT NULL,
        FrzInd      bit               NULL,
        CONSTRAINT pk_company PRIMARY KEY (CompanyCode)
    );
END
GO

-- ------------------------------------------------------------
-- Branch (legacy table — no-op if already exists)
-- ------------------------------------------------------------
IF OBJECT_ID('presets.dbo.Branch', 'U') IS NULL
BEGIN
    CREATE TABLE presets.dbo.Branch
    (
        BranchCode  nvarchar(10)   NOT NULL,
        CompanyCode nvarchar(10)   NOT NULL,
        BranchName  nvarchar(50)  NOT NULL,
        FrzInd      bit               NULL,
        CONSTRAINT pk_branch PRIMARY KEY (BranchCode, CompanyCode)
    );
END
GO

-- ------------------------------------------------------------
-- tenant_user_status (new Nova table)
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
-- Immutable columns: id, tenant_id, user_id, new_password_hash,
-- token_hash, expires_on, created_on.
-- confirmed_on is set once on confirmation.
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

-- Token confirmation lookup (unconfirmed only)
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
