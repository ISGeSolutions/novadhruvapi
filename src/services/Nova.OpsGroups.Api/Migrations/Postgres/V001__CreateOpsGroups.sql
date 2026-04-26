-- ============================================================
-- Nova.OpsGroups.Api — V001
-- Postgres dialect
-- All tables live in the presets schema (owned by Nova.Presets.Api).
-- This migration creates only the OpsGroups-specific tables:
--   tour_departures, grouptour_departure_group_tasks,
--   grouptour_task_business_rules
-- enquiry_events, tour_series, sla_task, sla_task_audit
-- are in Nova.Presets.Api V006.
-- ============================================================

CREATE SCHEMA IF NOT EXISTS presets;

-- ------------------------------------------------------------
-- tour_departures
-- Operational group-tour departure instances.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.tour_departures
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
    CONSTRAINT pk_tour_departures    PRIMARY KEY (id),
    CONSTRAINT uq_tour_departures_id UNIQUE (tenant_id, departure_id)
);

CREATE INDEX IF NOT EXISTS ix_tour_departures_date
    ON presets.tour_departures (tenant_id, departure_date);

CREATE INDEX IF NOT EXISTS ix_tour_departures_series
    ON presets.tour_departures (tenant_id, series_code);

CREATE INDEX IF NOT EXISTS ix_tour_departures_branch
    ON presets.tour_departures (tenant_id, branch_code);

-- ------------------------------------------------------------
-- grouptour_departure_group_tasks
-- Per-departure task instances generated from group_tasks templates.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.grouptour_departure_group_tasks
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
    CONSTRAINT pk_grouptour_departure_group_tasks    PRIMARY KEY (id),
    CONSTRAINT uq_grouptour_departure_group_tasks_id UNIQUE (tenant_id, departure_id, group_task_id)
);

CREATE INDEX IF NOT EXISTS ix_grouptour_departure_group_tasks_dep
    ON presets.grouptour_departure_group_tasks (tenant_id, departure_id);

-- ------------------------------------------------------------
-- grouptour_task_business_rules
-- Per-tenant/company/branch readiness and risk thresholds.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.grouptour_task_business_rules
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
