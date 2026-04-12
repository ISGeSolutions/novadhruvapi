-- ============================================================
-- Nova.CommonUX.Api — V003
-- MariaDB dialect
-- ============================================================

ALTER TABLE `nova_auth`.`tenant_user_auth`
    ADD COLUMN IF NOT EXISTS `must_change_password` TINYINT(1) NOT NULL DEFAULT 0;
