-- ============================================================
-- Nova.Presets.Api — V006
-- MSSQL dialect
-- Adds: enquiry_events (lookup + seed), tour_series,
--       sla_task, sla_task_audit
-- ============================================================

-- ------------------------------------------------------------
-- enquiry_events
-- ------------------------------------------------------------
IF OBJECT_ID('presets.enquiry_events', 'U') IS NULL
BEGIN
    CREATE TABLE presets.enquiry_events
    (
        code        varchar(5)   NOT NULL,
        description varchar(100) NOT NULL,
        sort_order  int          NOT NULL,
        CONSTRAINT pk_enquiry_events PRIMARY KEY (code)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM presets.enquiry_events WHERE code = 'DP')
    INSERT INTO presets.enquiry_events (code, description, sort_order) VALUES ('DP', 'Departure Date', 1);
IF NOT EXISTS (SELECT 1 FROM presets.enquiry_events WHERE code = 'RT')
    INSERT INTO presets.enquiry_events (code, description, sort_order) VALUES ('RT', 'Return Date', 2);
IF NOT EXISTS (SELECT 1 FROM presets.enquiry_events WHERE code = 'JI')
    INSERT INTO presets.enquiry_events (code, description, sort_order) VALUES ('JI', 'JI Date', 3);
GO

-- ------------------------------------------------------------
-- tour_series
-- ------------------------------------------------------------
IF OBJECT_ID('presets.tour_series', 'U') IS NULL
BEGIN
    CREATE TABLE presets.tour_series
    (
        id               uniqueidentifier NOT NULL,
        tenant_id        varchar(10)      NOT NULL,
        series_code      varchar(20)      NOT NULL,
        series_name      varchar(200)     NOT NULL,
        tour_generic_id  uniqueidentifier NOT NULL,
        frz_ind          bit              NOT NULL DEFAULT 0,
        created_by       varchar(10)      NOT NULL,
        created_on       datetime2        NOT NULL,
        updated_by       varchar(10)      NOT NULL,
        updated_on       datetime2        NOT NULL,
        updated_at       varchar(50)      NOT NULL,
        CONSTRAINT pk_tour_series          PRIMARY KEY (id),
        CONSTRAINT uq_tour_series_code     UNIQUE (tenant_id, series_code),
        CONSTRAINT fk_tour_series_tour_generics FOREIGN KEY (tour_generic_id)
            REFERENCES presets.tour_generics (id)
    );

    CREATE INDEX ix_tour_series_tenant
        ON presets.tour_series (tenant_id, frz_ind);
END
GO

-- ------------------------------------------------------------
-- sla_task
-- ------------------------------------------------------------
IF OBJECT_ID('presets.sla_task', 'U') IS NULL
BEGIN
    CREATE TABLE presets.sla_task
    (
        id              uniqueidentifier NOT NULL,
        tenant_id       varchar(10)      NOT NULL,
        scope_type      varchar(4)       NOT NULL,
        scope_id        uniqueidentifier NOT NULL,
        enq_event_code  varchar(5)       NOT NULL,
        task_code       varchar(10)      NOT NULL,
        kind            varchar(5)       NOT NULL,
        offset_days     int                  NULL,
        updated_by      varchar(10)      NOT NULL,
        updated_on      datetime2        NOT NULL,
        CONSTRAINT pk_sla_task                  PRIMARY KEY (id),
        CONSTRAINT uq_sla_task_scope_event_task UNIQUE (tenant_id, scope_type, scope_id, enq_event_code, task_code),
        CONSTRAINT fk_sla_task_enquiry_events   FOREIGN KEY (enq_event_code) REFERENCES presets.enquiry_events (code),
        CONSTRAINT chk_sla_task_scope_type      CHECK (scope_type IN ('GLOB','TG','TS','TD')),
        CONSTRAINT chk_sla_task_kind            CHECK (kind IN ('SET','NA')),
        CONSTRAINT chk_sla_task_offset          CHECK (
            (kind = 'SET' AND offset_days IS NOT NULL) OR
            (kind = 'NA'  AND offset_days IS NULL)
        )
    );

    CREATE INDEX ix_sla_task_scope
        ON presets.sla_task (tenant_id, scope_type, scope_id);
END
GO

-- ------------------------------------------------------------
-- sla_task_audit
-- ------------------------------------------------------------
IF OBJECT_ID('presets.sla_task_audit', 'U') IS NULL
BEGIN
    CREATE TABLE presets.sla_task_audit
    (
        id               uniqueidentifier NOT NULL,
        tenant_id        varchar(10)      NOT NULL,
        scope_type       varchar(4)       NOT NULL,
        scope_id         uniqueidentifier NOT NULL,
        enq_event_code   varchar(5)       NOT NULL,
        task_code        varchar(10)      NOT NULL,
        kind_old         varchar(5)           NULL,
        offset_days_old  int                  NULL,
        kind_new         varchar(5)           NULL,
        offset_days_new  int                  NULL,
        changed_by       varchar(10)      NOT NULL,
        changed_on       datetime2        NOT NULL,
        CONSTRAINT pk_sla_task_audit PRIMARY KEY (id)
    );

    CREATE INDEX ix_sla_task_audit_scope
        ON presets.sla_task_audit (tenant_id, scope_type, scope_id, changed_on DESC);
END
GO
