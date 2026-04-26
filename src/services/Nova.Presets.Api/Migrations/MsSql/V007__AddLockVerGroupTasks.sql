-- ============================================================
-- Nova.Presets.Api — V007
-- MSSQL dialect
-- Add lock_ver (optimistic concurrency token) to group_tasks.
-- Default 0 backfills all existing rows.
-- ============================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID('presets.group_tasks')
    AND    name      = 'lock_ver')
BEGIN
    ALTER TABLE presets.group_tasks
        ADD lock_ver int NOT NULL DEFAULT 0;
END
GO
