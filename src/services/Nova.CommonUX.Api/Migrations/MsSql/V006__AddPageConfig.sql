-- ============================================================
-- Nova.CommonUX.Api — V006
-- MSSQL dialect
-- Adds page_config table to nova_auth database.
-- ============================================================

IF OBJECT_ID('nova_auth.dbo.page_config', 'U') IS NULL
BEGIN
    CREATE TABLE nova_auth.dbo.page_config
    (
        program_code  varchar(100)  NOT NULL,
        config_json   nvarchar(max)     NULL,
        updated_by    varchar(10)   NOT NULL DEFAULT '',
        updated_on    datetime2     NOT NULL,
        CONSTRAINT pk_page_config PRIMARY KEY (program_code)
    );
END
GO
