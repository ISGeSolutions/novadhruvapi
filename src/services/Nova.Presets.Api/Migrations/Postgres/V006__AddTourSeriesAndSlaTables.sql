-- ============================================================
-- Nova.Presets.Api — V006
-- Postgres dialect
-- Adds: enquiry_events (lookup + seed), tour_series,
--       sla_task, sla_task_audit
-- ============================================================

-- ------------------------------------------------------------
-- enquiry_events
-- Lookup table for SLA reference-date event codes.
-- Seeded with the three canonical events; extend via migration.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.enquiry_events
(
    code        varchar(5)    NOT NULL,
    description varchar(100)  NOT NULL,
    sort_order  int           NOT NULL,
    CONSTRAINT pk_enquiry_events PRIMARY KEY (code)
);

INSERT INTO presets.enquiry_events (code, description, sort_order) VALUES
    ('DP', 'Departure Date', 1),
    ('RT', 'Return Date',    2),
    ('JI', 'JI Date',        3)
ON CONFLICT (code) DO NOTHING;

-- ------------------------------------------------------------
-- tour_series
-- Tenant-scoped Tour Series catalogue.
-- Linked to tour_generics; series_code unique per tenant.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.tour_series
(
    id               uuid         NOT NULL,
    tenant_id        varchar(10)  NOT NULL,
    series_code      varchar(20)  NOT NULL,
    series_name      varchar(200) NOT NULL,
    tour_generic_id  uuid         NOT NULL,
    frz_ind          boolean      NOT NULL DEFAULT false,
    created_by       varchar(10)  NOT NULL,
    created_on       timestamptz  NOT NULL,
    updated_by       varchar(10)  NOT NULL,
    updated_on       timestamptz  NOT NULL,
    updated_at       varchar(50)  NOT NULL,
    CONSTRAINT pk_tour_series            PRIMARY KEY (id),
    CONSTRAINT uq_tour_series_code       UNIQUE (tenant_id, series_code),
    CONSTRAINT fk_tour_series_tour_generics FOREIGN KEY (tour_generic_id)
        REFERENCES presets.tour_generics (id)
);

CREATE INDEX IF NOT EXISTS ix_tour_series_tenant
    ON presets.tour_series (tenant_id)
    WHERE frz_ind = false;

-- ------------------------------------------------------------
-- sla_task
-- Normalised SLA cell store.
-- scope_type: GLOB | TG | TS | TD
-- scope_id: UUID of the scope row (no FK — polymorphic).
-- kind: SET (explicit offset) | NA (not applicable).
-- Absence of a row means Inherit from parent scope.
-- CHECK ensures offset_days is set iff kind='SET'.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.sla_task
(
    id              uuid        NOT NULL,
    tenant_id       varchar(10) NOT NULL,
    scope_type      varchar(4)  NOT NULL,
    scope_id        uuid        NOT NULL,
    enq_event_code  varchar(5)  NOT NULL,
    task_code       varchar(10) NOT NULL,
    kind            varchar(5)  NOT NULL,
    offset_days     int             NULL,
    updated_by      varchar(10) NOT NULL,
    updated_on      timestamptz NOT NULL,
    CONSTRAINT pk_sla_task                    PRIMARY KEY (id),
    CONSTRAINT uq_sla_task_scope_event_task   UNIQUE (tenant_id, scope_type, scope_id, enq_event_code, task_code),
    CONSTRAINT fk_sla_task_enquiry_events     FOREIGN KEY (enq_event_code) REFERENCES presets.enquiry_events (code),
    CONSTRAINT chk_sla_task_scope_type        CHECK (scope_type IN ('GLOB','TG','TS','TD')),
    CONSTRAINT chk_sla_task_kind              CHECK (kind IN ('SET','NA')),
    CONSTRAINT chk_sla_task_offset            CHECK (
        (kind = 'SET' AND offset_days IS NOT NULL) OR
        (kind = 'NA'  AND offset_days IS NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_sla_task_scope
    ON presets.sla_task (tenant_id, scope_type, scope_id);

-- ------------------------------------------------------------
-- sla_task_audit
-- Full audit trail for every SLA cell change.
-- kind_old/kind_new NULL means "inherit" (no row existed/exists).
-- offset_days_old/new NULL when kind was/is 'NA' or 'inherit'.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS presets.sla_task_audit
(
    id               uuid        NOT NULL,
    tenant_id        varchar(10) NOT NULL,
    scope_type       varchar(4)  NOT NULL,
    scope_id         uuid        NOT NULL,
    enq_event_code   varchar(5)  NOT NULL,
    task_code        varchar(10) NOT NULL,
    kind_old         varchar(5)      NULL,
    offset_days_old  int             NULL,
    kind_new         varchar(5)      NULL,
    offset_days_new  int             NULL,
    changed_by       varchar(10) NOT NULL,
    changed_on       timestamptz NOT NULL,
    CONSTRAINT pk_sla_task_audit PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_sla_task_audit_scope
    ON presets.sla_task_audit (tenant_id, scope_type, scope_id, changed_on DESC);
