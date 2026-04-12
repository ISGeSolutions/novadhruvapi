-- ============================================================
-- V002 — Add enabled_auth_methods to tenant_config
-- Comma-separated list of auth methods enabled for this tenant.
-- NULL = all methods enabled (google, microsoft, apple, magic_link).
-- ============================================================

ALTER TABLE `nova_auth`.`tenant_config`
    ADD COLUMN IF NOT EXISTS `enabled_auth_methods` VARCHAR(200) NULL;
