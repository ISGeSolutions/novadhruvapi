-- ============================================================
-- V002 — Add enabled_auth_methods to tenant_config
-- Comma-separated list of auth methods enabled for this tenant.
-- NULL = all methods enabled (google, microsoft, apple, magic_link).
-- ============================================================

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
