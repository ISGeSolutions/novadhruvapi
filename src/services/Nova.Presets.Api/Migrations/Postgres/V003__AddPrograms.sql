-- ============================================================
-- Nova.Presets.Api — V003
-- Postgres dialect
-- ============================================================

-- ------------------------------------------------------------
-- programs
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.programs
(
    id                      varchar(10)     NOT NULL,
    name                    varchar(200)    NOT NULL,
    nav_type                varchar(20)     NOT NULL,
    route                   varchar(150)    NULL,
    external_url            varchar(150)    NULL,
    external_url_param_mode varchar(150)    NOT NULL DEFAULT 'none',
    icon                    varchar(100)    NULL,
    is_active               boolean         NOT NULL DEFAULT true,
    frz_ind                 boolean         NOT NULL DEFAULT false,
    created_by              varchar(10)     NOT NULL,
    created_on              timestamptz     NOT NULL,
    updated_by              varchar(10)     NOT NULL,
    updated_on              timestamptz     NOT NULL,
    updated_at              varchar(50)     NOT NULL,
    CONSTRAINT pk_programs PRIMARY KEY (id)
);

-- ------------------------------------------------------------
-- program_tree
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.program_tree
(
    program_id_parent   varchar(10)     NOT NULL,
    program_id_child    varchar(10)     NOT NULL,
    sort_order          integer         NOT NULL DEFAULT 0,
    frz_ind             boolean         NOT NULL DEFAULT false,
    created_by          varchar(10)     NOT NULL,
    created_on          timestamptz     NOT NULL,
    updated_by          varchar(10)     NOT NULL,
    updated_on          timestamptz     NOT NULL,
    updated_at          varchar(50)     NOT NULL,
    CONSTRAINT pk_program_tree PRIMARY KEY (program_id_parent, program_id_child)
);

-- ------------------------------------------------------------
-- Indexes
-- ------------------------------------------------------------

CREATE INDEX IF NOT EXISTS ix_program_tree_parent
    ON presets.program_tree (program_id_parent, sort_order)
    WHERE frz_ind = false;
