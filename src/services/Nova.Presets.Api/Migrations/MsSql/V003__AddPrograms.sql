-- ============================================================
-- Nova.Presets.Api — V003
-- MSSQL dialect
-- ============================================================

-- ------------------------------------------------------------
-- programs
-- Product-level navigation nodes. No tenant_id — menus are
-- defined at the product level. Users point to a root node
-- via tenant_user_profile.program_id_root.
-- ------------------------------------------------------------
IF OBJECT_ID('presets.dbo.programs', 'U') IS NULL
BEGIN
    CREATE TABLE presets.dbo.programs
    (
        id                      nvarchar(10)    NOT NULL,
        name                    nvarchar(200)   NOT NULL,
        nav_type                nvarchar(20)    NOT NULL,
        route                   nvarchar(150)   NULL,
        external_url            nvarchar(150)   NULL,
        external_url_param_mode nvarchar(150)   NOT NULL DEFAULT 'none',
        icon                    nvarchar(100)   NULL,
        is_active               bit             NOT NULL DEFAULT 1,
        frz_ind                 bit             NOT NULL DEFAULT 0,
        created_by              nvarchar(10)    NOT NULL,
        created_on              datetimeoffset  NOT NULL,
        updated_by              nvarchar(10)    NOT NULL,
        updated_on              datetimeoffset  NOT NULL,
        updated_at              varchar(50)     NOT NULL,
        CONSTRAINT pk_programs PRIMARY KEY (id)
    );
END
GO

-- ------------------------------------------------------------
-- program_tree
-- Adjacency list defining the parent/child relationships
-- between program nodes. sort_order governs display sequence
-- within each parent.
-- ------------------------------------------------------------
IF OBJECT_ID('presets.dbo.program_tree', 'U') IS NULL
BEGIN
    CREATE TABLE presets.dbo.program_tree
    (
        program_id_parent   nvarchar(10)    NOT NULL,
        program_id_child    nvarchar(10)    NOT NULL,
        sort_order          int             NOT NULL DEFAULT 0,
        frz_ind             bit             NOT NULL DEFAULT 0,
        created_by          nvarchar(10)    NOT NULL,
        created_on          datetimeoffset  NOT NULL,
        updated_by          nvarchar(10)    NOT NULL,
        updated_on          datetimeoffset  NOT NULL,
        updated_at          varchar(50)     NOT NULL,
        CONSTRAINT pk_program_tree PRIMARY KEY (program_id_parent, program_id_child)
    );
END
GO

-- ------------------------------------------------------------
-- Indexes
-- ------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_program_tree_parent'
               AND object_id = OBJECT_ID('presets.dbo.program_tree'))
    CREATE INDEX ix_program_tree_parent
        ON presets.dbo.program_tree (program_id_parent, sort_order)
        WHERE frz_ind = 0;
GO
