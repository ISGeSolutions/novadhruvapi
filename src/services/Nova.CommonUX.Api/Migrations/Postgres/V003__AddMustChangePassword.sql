-- ============================================================
-- Nova.CommonUX.Api — V003
-- Postgres dialect
-- ============================================================

ALTER TABLE nova_auth.tenant_user_auth
    ADD COLUMN IF NOT EXISTS must_change_password boolean NOT NULL DEFAULT false;
