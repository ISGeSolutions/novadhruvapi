-- ============================================================
-- Nova.CommonUX.Api — V004
-- MSSQL dialect
-- Adds program_id_root to tenant_user_profile.
-- Points to the root node of the user's navigation menu tree
-- (presets.dbo.programs). Set at provisioning; updated on role change.
-- ============================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID('nova_auth.dbo.tenant_user_profile')
    AND    name      = 'program_id_root')
BEGIN
    ALTER TABLE nova_auth.dbo.tenant_user_profile
        ADD program_id_root nvarchar(10) NULL;
END
GO
