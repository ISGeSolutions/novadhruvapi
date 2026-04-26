-- ============================================================
-- Nova.Presets.Api — V007
-- MariaDB dialect
-- Add lock_ver (optimistic concurrency token) to group_tasks.
-- Default 0 backfills all existing rows.
-- ============================================================

ALTER TABLE `presets`.`group_tasks`
    ADD COLUMN IF NOT EXISTS `lock_ver` int NOT NULL DEFAULT 0;
