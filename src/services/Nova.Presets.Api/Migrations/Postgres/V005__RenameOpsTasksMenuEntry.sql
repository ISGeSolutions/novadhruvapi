-- ============================================================
-- Nova.Presets.Api — V005
-- Postgres dialect
-- Renames the "Activity Templates" menu entry to "Task List"
-- and updates the route from /ops-admin/activities to
-- /ops-admin/tasks. If the entry does not yet exist it is
-- inserted so fresh and migrated installations both converge.
-- ============================================================

INSERT INTO presets.programs
    (id, name, nav_type, route, external_url, external_url_param_mode, icon, is_active, frz_ind,
     created_by, created_on, updated_by, updated_on, updated_at)
VALUES
    ('OPS_TASKS', 'Task List', 'link', '/ops-admin/tasks', NULL, 'none', 'list-checks',
     true, false,
     'migration', NOW(), 'migration', NOW(), 'V005__RenameOpsTasksMenuEntry')
ON CONFLICT (id) DO UPDATE SET
    name       = EXCLUDED.name,
    route      = EXCLUDED.route,
    updated_by = EXCLUDED.updated_by,
    updated_on = EXCLUDED.updated_on,
    updated_at = EXCLUDED.updated_at;

-- Remove the old OPS_ACTIVITIES entry if it still exists (replaced by OPS_TASKS above).
-- Cascade: remove its program_tree parent links first to avoid FK violations.
DELETE FROM presets.program_tree
WHERE  program_id_child = 'OPS_ACTIVITIES';

DELETE FROM presets.programs
WHERE  id = 'OPS_ACTIVITIES';
