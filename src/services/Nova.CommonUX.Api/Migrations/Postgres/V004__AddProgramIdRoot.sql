-- ============================================================
-- Nova.CommonUX.Api — V004
-- Postgres dialect
-- Adds program_id_root to tenant_user_profile.
-- Points to the root node of the user's navigation menu tree
-- (presets.programs). Set at provisioning; updated on role change.
-- ============================================================

ALTER TABLE nova_auth.tenant_user_profile
    ADD COLUMN IF NOT EXISTS program_id_root varchar(10) NULL;
