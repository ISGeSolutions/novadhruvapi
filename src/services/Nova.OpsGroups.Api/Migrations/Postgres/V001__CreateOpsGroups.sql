-- ============================================================
-- Nova.OpsGroups.Api — opsgroups schema
-- Postgres dialect
-- ============================================================

CREATE SCHEMA IF NOT EXISTS opsgroups;

-- ------------------------------------------------------------
-- grouptour_departures
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS opsgroups.grouptour_departures
(
    id                    uuid          NOT NULL,
    tenant_id             varchar(10)   NOT NULL,
    departure_id          varchar(50)   NOT NULL,
    series_code           varchar(20)   NOT NULL,
    series_name           varchar(200)  NOT NULL,
    departure_date        date          NOT NULL,
    return_date           date              NULL,
    destination_code      varchar(20)   NOT NULL,
    destination_name      varchar(200)  NOT NULL,
    branch_code           varchar(10)   NOT NULL,
    ops_manager_initials  varchar(10)   NOT NULL DEFAULT '',
    ops_manager_name      varchar(200)  NOT NULL DEFAULT '',
    ops_exec_initials     varchar(10)   NOT NULL DEFAULT '',
    ops_exec_name         varchar(200)  NOT NULL DEFAULT '',
    pax_count             int           NOT NULL DEFAULT 0,
    booking_count         int           NOT NULL DEFAULT 0,
    gtd                   boolean       NOT NULL DEFAULT false,
    notes                 varchar(2000)     NULL,
    frz_ind               boolean       NOT NULL DEFAULT false,
    created_by            varchar(10)   NOT NULL,
    created_on            timestamptz   NOT NULL,
    updated_by            varchar(10)   NOT NULL,
    updated_on            timestamptz   NOT NULL,
    updated_at            varchar(50)   NOT NULL,
    CONSTRAINT pk_grouptour_departures    PRIMARY KEY (id),
    CONSTRAINT uq_grouptour_departures_id UNIQUE (tenant_id, departure_id)
);

CREATE INDEX IF NOT EXISTS ix_grouptour_departures_date
    ON opsgroups.grouptour_departures (tenant_id, departure_date);

CREATE INDEX IF NOT EXISTS ix_grouptour_departures_series
    ON opsgroups.grouptour_departures (tenant_id, series_code);

CREATE INDEX IF NOT EXISTS ix_grouptour_departures_branch
    ON opsgroups.grouptour_departures (tenant_id, branch_code);

-- ------------------------------------------------------------
-- grouptour_departure_group_tasks
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS opsgroups.grouptour_departure_group_tasks
(
    id              uuid          NOT NULL,
    tenant_id       varchar(10)   NOT NULL,
    departure_id    varchar(50)   NOT NULL,
    group_task_id   varchar(50)   NOT NULL,
    template_code   varchar(10)   NOT NULL,
    status          varchar(30)   NOT NULL DEFAULT 'not_started',
    due_date        date              NULL,
    completed_date  date              NULL,
    notes           varchar(2000)     NULL,
    source          varchar(10)   NOT NULL DEFAULT 'GLOBAL',
    frz_ind         boolean       NOT NULL DEFAULT false,
    created_by      varchar(10)   NOT NULL,
    created_on      timestamptz   NOT NULL,
    updated_by      varchar(10)   NOT NULL,
    updated_on      timestamptz   NOT NULL,
    updated_at      varchar(50)   NOT NULL,
    CONSTRAINT pk_grouptour_departure_group_tasks      PRIMARY KEY (id),
    CONSTRAINT uq_grouptour_departure_group_tasks_id   UNIQUE (tenant_id, departure_id, group_task_id)
);

CREATE INDEX IF NOT EXISTS ix_grouptour_departure_group_tasks_dep
    ON opsgroups.grouptour_departure_group_tasks (tenant_id, departure_id);

-- ------------------------------------------------------------
-- grouptour_sla_rules
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS opsgroups.grouptour_sla_rules
(
    id                          uuid          NOT NULL,
    tenant_id                   varchar(10)   NOT NULL,
    level                       varchar(20)   NOT NULL,
    scope_key                   varchar(100)  NOT NULL,
    tour_code                   varchar(20)       NULL,
    group_task_code             varchar(10)   NOT NULL,
    reference_date              varchar(20)   NOT NULL,
    group_task_sla_offset_days  int               NULL,
    version                     varchar(50)       NULL,
    created_by                  varchar(10)   NOT NULL,
    created_on                  timestamptz   NOT NULL,
    updated_by                  varchar(10)   NOT NULL,
    updated_on                  timestamptz   NOT NULL,
    updated_at                  varchar(50)   NOT NULL,
    CONSTRAINT pk_grouptour_sla_rules PRIMARY KEY (id),
    CONSTRAINT uq_grouptour_sla_rules UNIQUE (tenant_id, scope_key, group_task_code, reference_date)
);

CREATE INDEX IF NOT EXISTS ix_grouptour_sla_rules_level
    ON opsgroups.grouptour_sla_rules (tenant_id, level, tour_code);

-- ------------------------------------------------------------
-- grouptour_sla_rule_audit
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS opsgroups.grouptour_sla_rule_audit
(
    id                uuid          NOT NULL,
    tenant_id         varchar(10)   NOT NULL,
    scope_key         varchar(100)  NOT NULL,
    scope_label       varchar(200)  NOT NULL,
    group_task_code   varchar(10)   NOT NULL,
    reference_date    varchar(20)   NOT NULL,
    old_value         int               NULL,
    new_value         int               NULL,
    changed_by_name   varchar(200)  NOT NULL,
    changed_at        timestamptz   NOT NULL,
    CONSTRAINT pk_grouptour_sla_rule_audit PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_grouptour_sla_rule_audit_scope
    ON opsgroups.grouptour_sla_rule_audit (tenant_id, scope_key, changed_at DESC);

-- ------------------------------------------------------------
-- grouptour_task_business_rules
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS opsgroups.grouptour_task_business_rules
(
    tenant_id               varchar(10)   NOT NULL,
    company_code            varchar(10)   NOT NULL,
    branch_code             varchar(10)   NOT NULL,
    overdue_critical_days   int           NOT NULL DEFAULT 3,
    overdue_warning_days    int           NOT NULL DEFAULT 7,
    readiness_method        varchar(32)   NOT NULL DEFAULT 'required_only',
    risk_red_threshold      varchar(64)   NOT NULL DEFAULT 'critical_overdue',
    risk_amber_threshold    varchar(64)   NOT NULL DEFAULT 'any_overdue',
    risk_green_threshold    varchar(64)   NOT NULL DEFAULT 'no_overdue',
    heatmap_red_max         int           NOT NULL DEFAULT 39,
    heatmap_amber_max       int           NOT NULL DEFAULT 79,
    auto_mark_overdue       boolean       NOT NULL DEFAULT true,
    include_na_in_readiness boolean       NOT NULL DEFAULT false,
    updated_at              timestamptz   NOT NULL,
    updated_by              varchar(10)   NOT NULL,
    CONSTRAINT pk_grouptour_task_business_rules PRIMARY KEY (tenant_id, company_code, branch_code)
);
