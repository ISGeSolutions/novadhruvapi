-- ============================================================
-- Nova.Presets.Api — V005
-- MariaDB dialect
-- Renames the "Activity Templates" menu entry to "Task List".
-- ============================================================

INSERT INTO `presets`.`programs`
    (`id`, `name`, `nav_type`, `route`, `external_url`, `external_url_param_mode`, `icon`, `is_active`, `frz_ind`,
     `created_by`, `created_on`, `updated_by`, `updated_on`, `updated_at`)
VALUES
    ('OPS_TASKS', 'Task List', 'link', '/ops-admin/tasks', NULL, 'none', 'list-checks',
     1, 0,
     'migration', UTC_TIMESTAMP(6), 'migration', UTC_TIMESTAMP(6), 'V005__RenameOpsTasksMenuEntry')
ON DUPLICATE KEY UPDATE
    `name`       = VALUES(`name`),
    `route`      = VALUES(`route`),
    `updated_by` = VALUES(`updated_by`),
    `updated_on` = VALUES(`updated_on`),
    `updated_at` = VALUES(`updated_at`);

DELETE FROM `presets`.`program_tree` WHERE `program_id_child` = 'OPS_ACTIVITIES';
DELETE FROM `presets`.`programs`     WHERE `id`               = 'OPS_ACTIVITIES';
