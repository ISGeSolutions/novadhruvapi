-- ============================================================
-- Nova.Presets.Api — V005
-- MSSQL dialect
-- Renames the "Activity Templates" menu entry to "Task List".
-- ============================================================

MERGE INTO presets.programs WITH (HOLDLOCK) AS target
USING (SELECT 'OPS_TASKS' AS id) AS source ON target.id = source.id
WHEN MATCHED THEN
    UPDATE SET name       = 'Task List',
               route      = '/ops-admin/tasks',
               updated_by = 'migration',
               updated_on = GETUTCDATE(),
               updated_at = 'V005__RenameOpsTasksMenuEntry'
WHEN NOT MATCHED THEN
    INSERT (id, name, nav_type, route, external_url, external_url_param_mode, icon, is_active, frz_ind,
            created_by, created_on, updated_by, updated_on, updated_at)
    VALUES ('OPS_TASKS', 'Task List', 'link', '/ops-admin/tasks', NULL, 'none', 'list-checks',
            1, 0,
            'migration', GETUTCDATE(), 'migration', GETUTCDATE(), 'V005__RenameOpsTasksMenuEntry');

DELETE FROM presets.program_tree WHERE program_id_child = 'OPS_ACTIVITIES';
DELETE FROM presets.programs     WHERE id               = 'OPS_ACTIVITIES';
