-- ============================================================
-- Nova.CommonUX.Api — V003
-- MSSQL dialect
-- ============================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID('nova_auth.dbo.tenant_user_auth')
    AND    name      = 'must_change_password')
BEGIN
    ALTER TABLE nova_auth.dbo.tenant_user_auth
        ADD must_change_password bit NOT NULL DEFAULT 0;
END
GO
