-- ============================================================
-- Nova.OpsGroups.Api — V002
-- Postgres dialect
-- Add lock_ver (optimistic concurrency token) to
-- grouptour_departure_group_tasks.
-- Default 0 backfills all existing rows.
-- ============================================================

ALTER TABLE presets.grouptour_departure_group_tasks
    ADD COLUMN IF NOT EXISTS lock_ver int NOT NULL DEFAULT 0;
