-- Nova Naming Registry
-- Canonical vocabulary and legacy MSSQL alias reference for the Nova platform.
--
-- PURPOSE
--   1. nova_db_column  — approved column names for ALL new tables.
--      Every column name used in any new migration must exist here first.
--      Query here before naming a new column to avoid drift (bkg_no, not booking_no).
--   2. nova_db_table   — canonical snake_case table name, short alias for SQL JOINs,
--      and legacy MSSQL table name where a legacy counterpart exists.
--   3. nova_db_column_legacy — for queries against legacy MSSQL tables, maps
--      legacy column names to canonical names (alias to apply in SQL).
--      e.g. BookingNo AS bkg_no — C# property BkgNo maps via MatchNamesWithUnderscores.
--
-- WORKFLOW
--   New column name    : INSERT into nova_db_column, regenerate .db, commit both files.
--   New table          : INSERT into nova_db_table, regenerate .db, commit both files.
--   Legacy mapping     : INSERT into nova_db_column_legacy, regenerate .db, commit both.
--
-- REGENERATE .db
--   sqlite3 dev-tools/naming-registry.db < dev-tools/naming-registry.sql
--
-- All changes must be made in this file. PRs are reviewed against diffs here.
-- The .db file is committed alongside for direct querying without regeneration.
--
-- NOTE: canonical_name on nova_db_table is UNIQUE. Table names are expected to be
-- unique across the platform — shared reference tables (company, branch) live in
-- presets and are not redefined in other services.

PRAGMA foreign_keys = ON;

DROP TABLE IF EXISTS nova_db_column_legacy;
DROP TABLE IF EXISTS nova_db_column;
DROP TABLE IF EXISTS nova_db_table;

-- ============================================================
-- SCHEMA
-- ============================================================

CREATE TABLE nova_db_table (
    id              INTEGER  PRIMARY KEY,
    canonical_name  TEXT     NOT NULL,
    short_alias     TEXT     NOT NULL,
    legacy_name     TEXT,
    description     TEXT,
    CONSTRAINT uq_nova_db_table_canonical_name  UNIQUE (canonical_name),
    CONSTRAINT uq_nova_db_table_short_alias     UNIQUE (short_alias),
    CONSTRAINT uq_nova_db_table_legacy_name     UNIQUE (legacy_name)
);

CREATE TABLE nova_db_column (
    id              INTEGER  PRIMARY KEY,
    canonical_name  TEXT     NOT NULL,
    description     TEXT,
    CONSTRAINT uq_nova_db_column_canonical_name UNIQUE (canonical_name)
);

CREATE TABLE nova_db_column_legacy (
    id                  INTEGER  PRIMARY KEY,
    table_id            INTEGER  NOT NULL,
    legacy_column_name  TEXT     NOT NULL,
    column_id           INTEGER  NOT NULL,
    description         TEXT,
    CONSTRAINT fk_nova_db_column_legacy_table   FOREIGN KEY (table_id)  REFERENCES nova_db_table(id),
    CONSTRAINT fk_nova_db_column_legacy_column  FOREIGN KEY (column_id) REFERENCES nova_db_column(id),
    CONSTRAINT uq_nova_db_column_legacy_01      UNIQUE (table_id, legacy_column_name)
);

CREATE INDEX ix_nova_db_column_legacy_table_id  ON nova_db_column_legacy (table_id);
CREATE INDEX ix_nova_db_column_legacy_column_id ON nova_db_column_legacy (column_id);

-- ============================================================
-- SEED: nova_db_table
-- Populated from migrations as of 14 Apr 2026.
-- legacy_name = NULL for all current tables (all are new snake_case tables).
-- Legacy MSSQL counterparts to be added when legacy query work begins.
-- Schema prefix shown in description — canonical_name is table name only.
-- ============================================================

INSERT INTO nova_db_table (id, canonical_name, short_alias, legacy_name, description) VALUES
    -- nova_auth schema (Nova.CommonUX.Api)
    ( 1, 'tenant_secrets',                  'tsec', NULL, '[nova_auth] Client secret credentials per tenant'),
    ( 2, 'tenant_user_auth',                'tua',  NULL, '[nova_auth] Authentication state per user — password, TOTP, lockout'),
    ( 3, 'tenant_user_profile',             'tup',  NULL, '[nova_auth] Display profile per user — email, display name, avatar'),
    ( 4, 'tenant_user_social_identity',     'tusi', NULL, '[nova_auth] OAuth / social provider links per user'),
    ( 5, 'tenant_auth_tokens',              'tat',  NULL, '[nova_auth] Issued tokens — magic links, TOTP challenges, resets'),
    ( 6, 'tenant_config',                   'tcfg', NULL, '[nova_auth] Tenant UI configuration — branding, layout, auth methods'),
    ( 7, 'tenant_menu_items',               'tmi',  NULL, '[nova_auth] Navigation menu items per tenant'),
    -- presets schema (Nova.Presets.Api)
    ( 8, 'company',                         'co',   'Company', '[presets] Company within a tenant'),
    ( 9, 'branch',                          'br',   'Branch',  '[presets] Branch within a company'),
    (10, 'tenant_user_status',              'tus',  NULL, '[presets] Current availability / status for a user'),
    (11, 'tenant_password_change_requests', 'tpcr', NULL, '[presets] Pending password change requests with expiry'),
    (12, 'user_status_options',             'uso',  NULL, '[presets] Configurable user status options per company / branch tier'),
    -- dhruvlog schema (Nova.Shell.Api)
    (13, 'nova_outbox',                     'nox',  NULL, '[dhruvlog] Outbox relay — events pending dispatch to message broker'),
    -- sales97 schema (Nova.ToDo.Api)
    (14, 'todo',                            'tdo',  'ToDo', '[sales97] Task / to-do items linked to bookings, clients, or tours');

-- ============================================================
-- SEED: nova_db_column
-- Canonical vocabulary — every column name used in any new migration must be here.
-- Populated from all migrations as of 14 Apr 2026.
-- ============================================================

INSERT INTO nova_db_column (id, canonical_name, description) VALUES

    -- Common / audit columns
    ( 1, 'id',                           'UUID v7 primary key — new tables with no legacy MSSQL counterpart (app-generated before INSERT)'),
    ( 2, 'tenant_id',                    'Tenant identifier — multi-tenancy discriminator'),
    ( 3, 'user_id',                      'User identifier within a tenant'),
    ( 4, 'company_code',                 'Company code within a tenant'),
    ( 5, 'branch_code',                  'Branch code within a company'),
    ( 6, 'frz_ind',                      'Freeze indicator — record is soft-locked and read-only once frozen'),
    ( 7, 'is_active',                    'Active flag — used in menus and reference data'),
    ( 8, 'created_by',                   'User id who created the record'),
    ( 9, 'created_on',                   'UTC timestamp when the record was created — standard audit column (timestamptz)'),
    (10, 'created_at',                   'UTC timestamp when the record was created — used in outbox / event tables (timestamptz)'),
    (11, 'updated_by',                   'User id who last updated the record'),
    (12, 'updated_on',                   'UTC timestamp when the record was last updated (timestamptz)'),
    (13, 'updated_at',                   'Optimistic concurrency token — ISO string refreshed on every write (varchar)'),
    (14, 'sort_order',                   'Display sort order within a group'),
    (15, 'serial_no',                    'Display sequence number within a scoped group'),
    (16, 'label',                        'Display label for a reference item'),
    (17, 'colour',                       'Display colour code (hex or named) for a reference item'),

    -- Authentication
    (18, 'client_secret_hash',           'Hashed client secret for tenant API authentication'),
    (19, 'password_hash',                'Bcrypt hash of the user password'),
    (20, 'totp_enabled',                 'Whether TOTP two-factor authentication is enabled for this user'),
    (21, 'totp_secret_encrypted',        'Encrypted TOTP shared secret'),
    (22, 'failed_login_count',           'Count of consecutive failed login attempts'),
    (23, 'locked_until',                 'Timestamp until which the account is locked out'),
    (24, 'last_login_on',                'Timestamp of the last successful login'),
    (25, 'must_change_password',         'Flag — user must change password on next login'),
    (26, 'token_hash',                   'Hashed token value'),
    (27, 'token_type',                   'Token purpose type (magic_link, totp_challenge, reset, etc.)'),
    (28, 'expires_on',                   'Timestamp when the token or request expires'),
    (29, 'used_on',                      'Timestamp when the token was consumed'),
    (30, 'enabled_auth_methods',         'Comma-separated list of enabled auth methods for tenant (NULL = all enabled)'),

    -- User profile / social identity
    (31, 'email',                        'User email address'),
    (32, 'display_name',                 'Human-readable display name'),
    (33, 'avatar_url',                   'URL of the user avatar image'),
    (34, 'provider',                     'OAuth identity provider name (google, microsoft, apple)'),
    (35, 'provider_user_id',             'Unique user id from the OAuth provider'),
    (36, 'provider_email',               'Email address returned by the OAuth provider'),
    (37, 'linked_on',                    'Timestamp when the social identity was linked'),

    -- Tenant configuration / menu
    (38, 'tenant_name',                  'Display name for the tenant'),
    (39, 'company_name',                 'Display name for the company'),
    (40, 'branch_name',                  'Display name for the branch'),
    (41, 'client_name',                  'Full client or customer name'),
    (42, 'client_logo_url',              'URL of the client logo'),
    (43, 'active_users_inline_threshold','Max active users to display inline before switching to paginated view'),
    (44, 'unclosed_web_enquiries_url',   'URL for the unclosed web enquiries widget'),
    (45, 'task_list_url',                'URL for the task list widget'),
    (46, 'breadcrumb_position',          'Position of breadcrumbs in the UI layout (inline, top, etc.)'),
    (47, 'footer_gradient_refresh_ms',   'Refresh interval for the footer gradient animation in milliseconds'),
    (48, 'menu_item_id',                 'Unique identifier for a navigation menu item'),
    (49, 'parent_id',                    'Parent menu item id — used for nested / hierarchical navigation'),
    (50, 'icon',                         'Icon identifier or class name for a menu item'),
    (51, 'route',                        'Internal application route path'),
    (52, 'external_url_template',        'URL template for external links — may contain substitution tokens'),
    (53, 'external_url_param_mode',      'How URL parameters are appended to external links (none, query, path)'),
    (54, 'required_roles',               'Comma-separated roles required to see or access this item'),

    -- Presets
    (55, 'status_id',                    'Unique identifier for a user status record'),
    (56, 'status_code',                  'Code for a configurable user status option'),
    (57, 'status_label',                 'Display label for a user status option'),
    (58, 'status_note',                  'Optional free-text note on the current user status'),
    (59, 'new_password_hash',            'Hashed new password pending user confirmation'),
    (60, 'confirmed_on',                 'Timestamp when a pending request or action was confirmed'),

    -- Outbox / messaging
    (61, 'aggregate_id',                 'ID of the domain entity that triggered the event'),
    (62, 'event_type',                   'Fully qualified event type name'),
    (63, 'payload',                      'JSON event payload'),
    (64, 'processed_at',                 'Timestamp when the message was successfully dispatched'),
    (65, 'retry_count',                  'Number of dispatch attempts made so far'),
    (66, 'last_error',                   'Error message from the most recent failed dispatch attempt'),
    (67, 'exchange',                     'Message broker exchange name'),
    (68, 'routing_key',                  'Message broker routing key'),
    (69, 'content_type',                 'MIME type of the message payload'),
    (70, 'max_retries',                  'Maximum number of dispatch attempts allowed before marking as failed'),
    (71, 'status',                       'Outbox message status (pending, processed, failed, etc.)'),
    (72, 'scheduled_on',                 'Timestamp when a scheduled message should be dispatched'),
    (73, 'correlation_id',               'Correlation id for distributed tracing'),

    -- ToDo / task domain
    (74, 'job_code',                     'Booking or job code the task is related to'),
    (75, 'task_detail',                  'Full description of the task'),
    (76, 'assigned_to_user_code',        'User code of the assignee'),
    (77, 'priority_code',                'Priority level code (H, M, L or similar)'),
    (78, 'due_date',                     'Calendar date by which the task must be completed (date, no time component)'),
    (79, 'due_time',                     'Time-of-day (HH:mm) by which the task must be completed — stored as varchar(5)'),
    (80, 'inflexible_ind',               'Indicator — task cannot be rescheduled'),
    (81, 'start_date',                   'Calendar date from which the task may be started (date, no time component)'),
    (82, 'start_time',                   'Time-of-day (HH:mm) from which the task may be started — stored as varchar(5)'),
    (83, 'assigned_by_user_code',        'User code of the user who assigned the task'),
    (84, 'assigned_on',                  'Timestamp when the task was assigned'),
    (85, 'remark',                       'Free-text notes on the task'),
    (86, 'est_job_time',                 'Estimated time to complete the task (HH:mm) — stored as varchar(5)'),
    (87, 'bkg_no',                       'Booking reference number — canonical short form, not booking_no or booking_number'),
    (88, 'quote_no',                     'Quote number linked to the task'),
    (89, 'campaign_code',                'Campaign code linked to the task'),
    (90, 'account_code_client',          'Client account code linked to the task'),
    (91, 'tour_series_code',             'Tour series code identifying a series of departures for a tour product'),
    (92, 'dep_date',                     'Departure date linked to the task (date, no time component)'),
    (93, 'supplier_code',                'Supplier code linked to the task'),
    (94, 'send_email_to_ind',            'Indicator — send task notification email to assignee'),
    (95, 'sent_mail_ind',                'Indicator — notification email has been sent'),
    (96, 'alert_to_ind',                 'Indicator — show an in-app alert to the assignee'),
    (97, 'send_sms_ind',                 'Indicator — send SMS notification to assignee'),
    (98, 'send_sms_to',                  'Mobile number for SMS notification'),
    (99, 'travel_pnr_no',                'Travel PNR (Passenger Name Record) linked to the task'),
   (100, 'seq_no_charges',               'Charges sequence number linked to the task'),
   (101, 'seq_no_acct_notes',            'Account notes sequence number linked to the task'),
   (102, 'itinerary_no',                 'Itinerary number linked to the task'),
   (103, 'done_ind',                     'Indicator — task has been completed'),
   (104, 'done_by',                      'User code who marked the task as done'),
   (105, 'done_on',                      'Timestamp when the task was marked as done'),

    -- Canonical name for the legacy MSSQL integer identity PK (SeqNo).
    -- New tables use id uuid. Legacy-mirroring DTOs carry both Guid Id and int SeqNo.
   (106, 'seq_no',                       'Legacy MSSQL integer identity PK equivalent. New tables use id uuid instead. DTOs for legacy-mirroring tables carry both Guid Id and int SeqNo — only one is populated per dialect.');

-- ============================================================
-- SEED: nova_db_column_legacy
-- Format: (table_id, legacy_column_name, column_id, description)
-- description = NULL means MatchNamesWithUnderscores auto-maps — no alias needed.
-- description is populated where an explicit alias or type conversion is required.
-- ============================================================

-- presets.dbo.Company  (table_id = 8)
-- All columns auto-map via MatchNamesWithUnderscores.
-- FrzInd is nullable bit in legacy — alias with ISNULL to coerce to non-null bool.
INSERT INTO nova_db_column_legacy (table_id, legacy_column_name, column_id, description) VALUES
    (8, 'CompanyCode', 4,  NULL),
    (8, 'TenantId',    2,  NULL),
    (8, 'CompanyName', 39, NULL),
    (8, 'FrzInd',      6,  'Nullable bit in legacy MSSQL. Alias: ISNULL(FrzInd, 0) AS frz_ind');

-- presets.dbo.Branch  (table_id = 9)
-- All columns auto-map via MatchNamesWithUnderscores.
-- FrzInd is nullable bit in legacy — alias with ISNULL to coerce to non-null bool.
INSERT INTO nova_db_column_legacy (table_id, legacy_column_name, column_id, description) VALUES
    (9, 'BranchCode',  5,  NULL),
    (9, 'CompanyCode', 4,  NULL),
    (9, 'BranchName',  40, NULL),
    (9, 'FrzInd',      6,  'Nullable bit in legacy MSSQL. Alias: ISNULL(FrzInd, 0) AS frz_ind');

-- sales97.dbo.ToDo  (table_id = 14)
-- Aliases required: Brochure_Code_Short, DueTime, StartTime, EstJobTime.
-- SeqNo is the legacy int IDENTITY PK — not aliased. Legacy MSSQL DTO uses int SeqNo.
-- New tables use id uuid — separate DTO for legacy MSSQL queries.
INSERT INTO nova_db_column_legacy (table_id, legacy_column_name, column_id, description) VALUES
    (14, 'SeqNo',              106, 'int IDENTITY PK. Auto-maps via MatchNamesWithUnderscores — no alias needed. DTO has both int SeqNo (populated for MSSQL) and Guid Id (populated for Postgres/MariaDB).'),
    (14, 'JobCode',            74,  NULL),
    (14, 'TaskDetail',         75,  NULL),
    (14, 'AssignedToUserCode', 76,  NULL),
    (14, 'PriorityCode',       77,  NULL),
    (14, 'DueDate',            78,  NULL),
    (14, 'DueTime',            79,  'Stored as datetime. Alias: FORMAT(DueTime, ''HH:mm'') AS due_time'),
    (14, 'InFlexibleInd',      80,  NULL),
    (14, 'StartDate',          81,  NULL),
    (14, 'StartTime',          82,  'Stored as datetime. Alias: FORMAT(StartTime, ''HH:mm'') AS start_time'),
    (14, 'AssignedByUserCode', 83,  NULL),
    (14, 'AssignedOn',         84,  NULL),
    (14, 'Remark',             85,  NULL),
    (14, 'EstJobTime',         86,  'Stored as datetime. Alias: FORMAT(EstJobTime, ''HH:mm'') AS est_job_time'),
    (14, 'ClientName',         41,  NULL),
    (14, 'BkgNo',              87,  NULL),
    (14, 'QuoteNo',            88,  NULL),
    (14, 'CampaignCode',       89,  NULL),
    (14, 'Accountcode_Client', 90,  NULL),
    (14, 'Brochure_Code_Short',91,  'Different canonical name. Alias required: Brochure_Code_Short AS tour_series_code'),
    (14, 'DepDate',            92,  NULL),
    (14, 'SupplierCode',       93,  NULL),
    (14, 'SendEMailToInd',     94,  NULL),
    (14, 'SentMailInd',        95,  NULL),
    (14, 'AlertToInd',         96,  NULL),
    (14, 'SendSMSInd',         97,  NULL),
    (14, 'SendSMSTo',          98,  NULL),
    (14, 'Travel_PNRNo',       99,  NULL),
    (14, 'SeqNo_Charges',      100, NULL),
    (14, 'SeqNo_AcctNotes',    101, NULL),
    (14, 'Itinerary_No',       102, NULL),
    (14, 'DoneInd',            103, NULL),
    (14, 'DoneBy',             104, NULL),
    (14, 'DoneOn',             105, NULL),
    (14, 'FrzInd',             6,   NULL),
    (14, 'CreatedBy',          8,   NULL),
    (14, 'CreatedOn',          9,   'Stored as datetime (not datetime2) in legacy MSSQL. See docs/datetime-design.md.'),
    (14, 'UpdatedBy',          11,  NULL),
    (14, 'UpdatedOn',          12,  'Stored as datetime (not datetime2) in legacy MSSQL. See docs/datetime-design.md.'),
    (14, 'UpdatedAt',          13,  NULL);
