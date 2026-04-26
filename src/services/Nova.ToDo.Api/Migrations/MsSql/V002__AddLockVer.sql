-- V002: Add lock_ver column for optimistic concurrency (MSSQL)
-- See docs/concurrency-field-group-versioning.md for the full pattern.
-- Safe to run automatically — ALTER TABLE ADD with DEFAULT 0 is non-destructive.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID('sales97.dbo.ToDo')
    AND    name      = 'lock_ver'
)
BEGIN
    ALTER TABLE sales97.dbo.ToDo
        ADD lock_ver int NOT NULL DEFAULT 0;
END
