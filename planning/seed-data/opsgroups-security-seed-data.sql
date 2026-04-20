-- ============================================================
-- OpsGroups security seed data
-- Seeds user_security_role_flag definitions and test user rights
-- for BTDK tenant. Run V005 migration first.
-- ============================================================
-- User 'ISG' is seeded with both OPSMGR and OPSEXEC roles so
-- the Team Members endpoint can be tested with a multi-role user.
-- Add additional users by inserting into nova_auth.tenant_user_profile
-- first, then add rows here.
-- ============================================================

-- ============================================================
-- MSSQL
-- ============================================================

-- Role flag definitions — OPSMGR
IF NOT EXISTS (SELECT 1 FROM nova_auth.dbo.user_security_role_flag WHERE role_code = 'OPSMGR')
BEGIN
    INSERT INTO nova_auth.dbo.user_security_role_flag (role_code, flag_position, flag_name, flag_notes)
    VALUES
        ('OPSMGR', 1, 'is_ops_manager',     'Y = user is an Ops Manager for group tour task management'),
        ('OPSMGR', 2, 'can_change_due_date', 'Y = user can override default SLA due dates on activities');
END
GO

-- Role flag definitions — OPSEXEC
IF NOT EXISTS (SELECT 1 FROM nova_auth.dbo.user_security_role_flag WHERE role_code = 'OPSEXEC')
BEGIN
    INSERT INTO nova_auth.dbo.user_security_role_flag (role_code, flag_position, flag_name, flag_notes)
    VALUES
        ('OPSEXEC', 1, 'is_ops_exec',             'Y = user is an Ops Executive for group tour task management'),
        ('OPSEXEC', 2, 'can_reassign_activities',  'Y = user can reassign activities to other team members');
END
GO

-- Test user rights — ISG as OPSMGR (global scope: all companies, all branches)
IF NOT EXISTS (SELECT 1 FROM nova_auth.dbo.user_security_rights
               WHERE tenant_id = 'BTDK' AND user_id = 'ISG' AND role_code = 'OPSMGR')
BEGIN
    INSERT INTO nova_auth.dbo.user_security_rights
        (id, tenant_id, user_id, role_code, role_flags, company_code, branch_code,
         frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
    VALUES
        (NEWID(), 'BTDK', 'ISG', 'OPSMGR', 'YY', 'XXXX', 'XXXX',
         0, 'seed', GETUTCDATE(), 'seed', GETUTCDATE(), 'seed');
END
GO

-- Test user rights — ISG as OPSEXEC (global scope)
IF NOT EXISTS (SELECT 1 FROM nova_auth.dbo.user_security_rights
               WHERE tenant_id = 'BTDK' AND user_id = 'ISG' AND role_code = 'OPSEXEC')
BEGIN
    INSERT INTO nova_auth.dbo.user_security_rights
        (id, tenant_id, user_id, role_code, role_flags, company_code, branch_code,
         frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
    VALUES
        (NEWID(), 'BTDK', 'ISG', 'OPSEXEC', 'YN', 'XXXX', 'XXXX',
         0, 'seed', GETUTCDATE(), 'seed', GETUTCDATE(), 'seed');
END
GO

-- ============================================================
-- Postgres
-- ============================================================

-- Role flag definitions
INSERT INTO nova_auth.user_security_role_flag (role_code, flag_position, flag_name, flag_notes)
VALUES
    ('OPSMGR',  1, 'is_ops_manager',          'Y = user is an Ops Manager for group tour task management'),
    ('OPSMGR',  2, 'can_change_due_date',      'Y = user can override default SLA due dates on activities'),
    ('OPSEXEC', 1, 'is_ops_exec',              'Y = user is an Ops Executive for group tour task management'),
    ('OPSEXEC', 2, 'can_reassign_activities',  'Y = user can reassign activities to other team members')
ON CONFLICT (role_code, flag_position) DO NOTHING;

-- Test user rights
INSERT INTO nova_auth.user_security_rights
    (id, tenant_id, user_id, role_code, role_flags, company_code, branch_code,
     frz_ind, created_by, created_on, updated_by, updated_on, updated_at)
VALUES
    (gen_random_uuid(), 'BTDK', 'ISG', 'OPSMGR',  'YY', 'XXXX', 'XXXX',
     false, 'seed', NOW(), 'seed', NOW(), 'seed'),
    (gen_random_uuid(), 'BTDK', 'ISG', 'OPSEXEC', 'YN', 'XXXX', 'XXXX',
     false, 'seed', NOW(), 'seed', NOW(), 'seed')
ON CONFLICT (tenant_id, user_id, role_code, company_code, branch_code) DO NOTHING;

-- ============================================================
-- MariaDB
-- ============================================================

-- Role flag definitions
INSERT IGNORE INTO `nova_auth`.`user_security_role_flag` (`role_code`, `flag_position`, `flag_name`, `flag_notes`)
VALUES
    ('OPSMGR',  1, 'is_ops_manager',         'Y = user is an Ops Manager for group tour task management'),
    ('OPSMGR',  2, 'can_change_due_date',     'Y = user can override default SLA due dates on activities'),
    ('OPSEXEC', 1, 'is_ops_exec',             'Y = user is an Ops Executive for group tour task management'),
    ('OPSEXEC', 2, 'can_reassign_activities', 'Y = user can reassign activities to other team members');

-- Test user rights
INSERT IGNORE INTO `nova_auth`.`user_security_rights`
    (`id`, `tenant_id`, `user_id`, `role_code`, `role_flags`, `company_code`, `branch_code`,
     `frz_ind`, `created_by`, `created_on`, `updated_by`, `updated_on`, `updated_at`)
VALUES
    (UUID(), 'BTDK', 'ISG', 'OPSMGR',  'YY', 'XXXX', 'XXXX',
     0, 'seed', UTC_TIMESTAMP(), 'seed', UTC_TIMESTAMP(), 'seed'),
    (UUID(), 'BTDK', 'ISG', 'OPSEXEC', 'YN', 'XXXX', 'XXXX',
     0, 'seed', UTC_TIMESTAMP(), 'seed', UTC_TIMESTAMP(), 'seed');
