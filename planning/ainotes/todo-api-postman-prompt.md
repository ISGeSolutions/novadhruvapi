# Nova.ToDo.Api — Postman Contract Generation Prompt

Generate a detailed description of JSON request/response structures for all endpoints below, suitable for building a Postman collection and mock server. Do not generate C# or any other implementation code — the API contract only.

---

## Approach

1. Create JSON structure of request/response, importable into Postman. Include sample data covering all relevant HTTP response codes.
2. Convert to Postman Mock Server.
3. UX team (React) uses the mock to build against.
4. The Postman JSON is then used to generate the dotnet API service (`src/services/Nova.ToDo.Api`) via AI.

Port: **5101**

---

## Platform Conventions (apply throughout)

### Primary Keys and ID Types

- **MSSQL**: `SeqNo INT IDENTITY(1,1)` — DB-generated integer, existing convention
- **MySQL / MariaDB**: `SeqNo INT AUTO_INCREMENT` — DB-generated integer
- **Postgres**: `id uuid` — UUID v7, app-generated

All API responses carry the primary key as a **`string`** in the JSON, regardless of DB engine (the API layer parses to `int` or `Guid` internally). In mock data, use an integer string for MSSQL/MySQL examples (e.g. `"seq_no": "1042"`) — the Postman collection targets MSSQL unless noted.

### Standard Audit Columns (wire names)

Every table has these columns. Wire names (snake_case) are:

| Wire name    | DB column   | Notes                                      |
|--------------|-------------|--------------------------------------------|
| `frz_ind`    | `FrzInd`    | Soft-delete flag. `true` = frozen/deleted  |
| `created_on` | `CreatedOn` | Set on insert, never updated               |
| `created_by` | `CreatedBy` | User code of creator                       |
| `updated_on` | `UpdatedOn` | Set on every write (insert and update)     |
| `updated_by` | `UpdatedBy` | User code of last writer                   |
| `updated_at` | `UpdatedAt` | Client IP address string — not a datetime  |

`updated_at` stores the client IP address of the calling system — it is **not** a timestamp.

### Wire Format

All JSON keys use **snake_case** on the wire (e.g. `tenant_id`, `page_size`, `booking_no`). The frontend converts internally; service files never see snake_case directly.

### Authentication

All endpoints require a JWT Bearer token:

```
Authorization: Bearer <token>
```

**Obtain token:**

```
POST /api/v1/auth/token
Body: { "tenant_id": "T001", "shared_password": "..." }
Response: { "token": "eyJhbG...", "expires_in": 3600 }
```

The auth endpoint does not receive auto-injected context fields.

### HTTP Method Convention

- All data-retrieval endpoints use **POST** (not GET). Filters go in the JSON body — not in URL query strings.
- Partial updates use **PATCH** (`PATCH /api/v1/todos/{seq_no}`).

### Auto-Injected Context Fields

Every POST/PATCH body is automatically enriched by the API client. These fields must appear in every request example. They must not be specified manually by callers.

**API context (from tenant configuration):**

| Field        | Type   | Description                  |
|--------------|--------|------------------------------|
| `tenant_id`  | string | Current tenant identifier    |
| `company_id` | string | Current company identifier   |
| `branch_id`  | string | Current branch identifier    |
| `user_id`    | string | Current user identifier      |

**Client context (captured on startup):**

| Field              | Type          | Description                                            |
|--------------------|---------------|--------------------------------------------------------|
| `browser_locale`   | string        | e.g. `"en-GB"`                                         |
| `browser_timezone` | string        | IANA timezone, e.g. `"Europe/London"`                  |
| `ip_address`       | string / null | Client IP (fallback null). Server also reads `X-Forwarded-For`. |

In endpoint examples below, these 7 fields are marked `// ← auto-injected`.

### Naming Conventions

- Endpoints: kebab-case, plural nouns — `/api/v1/todos`, `/api/v1/booking-rows`
- Sub-resources: path-nested — `/api/v1/bookings/{booking_no}/passengers/{passenger_id}`
- Version prefix: `/api/v{n}/` — per-endpoint versioning supported

---

## Table Definition

```sql
CREATE TABLE sales97.dbo.ToDo (
    SeqNo               int IDENTITY(1,1) NOT NULL,     -- (1)
    JobCode             nvarchar(4)  NOT NULL,           -- (WH) Mandatory. ToDo Job Code
    TaskDetail          nvarchar(255) NOT NULL,          -- (Welcome home call) Mandatory. Task detail
    AssignedToUserCode  nvarchar(10) NOT NULL,           -- (JOEB) Mandatory. Assigned to user code
    PriorityCode        nvarchar(2)  NOT NULL,           -- (N) Mandatory. H=High, N=Normal, L=Low
    DueDate             datetime     NOT NULL,           -- (2026-10-01) Mandatory. Due date
    DueTime             datetime,                        -- (10:30) Time-sensitive tasks only
    InFlexibleInd       bit          DEFAULT ((0)) NOT NULL, -- (0) Optional. Must complete on due date
    StartDate           datetime,                        -- (yyyy-mm-dd) Optional. Rarely used
    StartTime           datetime,                        -- (HH:mm) Optional. Rarely used
    AssignedByUserCode  nvarchar(10) NOT NULL,           -- (JaneB) Mandatory. Assigned by user
    AssignedOn          datetime,                        -- (yyyy-MM-ddThh:mm:ssZ) Date task assigned
    Remark              nvarchar(max),                   -- Optional comments/notes
    EstJobTime          datetime,                        -- (HH:mm) Duration stored as datetime quirk

    ClientName          nvarchar(60),                    -- Used only when caller is not our CRM database
    BkgNo               int,                             -- (500310) Booking No (if linked)
    QuoteNo             int,                             -- (100234) Quote/Enquiry No (EnquiryNo = QuoteNo)
    CampaignCode        nvarchar(16),                    -- (FACEBOOK) Marketing source
    Accountcode_Client  nvarchar(10),                    -- (0000123456) CRM client. Leading zeros must be retained
    Brochure_Code_Short nvarchar(10),                    -- (EXP2026) Also known as TourSeriesCode. Group tour link. Requires DepDate.
    DepDate             datetime,                        -- (2026-12-15) Group tour departure. Requires Brochure_Code_Short.
    SupplierCode        nvarchar(10),                    -- (0000000001) Supplier. Leading zeros must be retained.

    SendEMailToInd      bit DEFAULT ((0)) NOT NULL,      -- Optional. Queue email to assigned user
    SentMailInd         bit,                             -- True once email sent
    AlertToInd          bit DEFAULT ((0)) NOT NULL,      -- Optional. Queue notification to assigned user
    SendSMSInd          bit DEFAULT ((0)) NOT NULL,      -- Optional. Queue SMS to assigned user
    SendSMSTo           nvarchar(20),                    -- Mobile number. Mandatory if SendSMSInd = true

    -- Task-source fields (duplicate prevention). All optional. Only one may be set per record.
    Travel_PNRNo        nvarchar(25),                    -- (XY2RP6) PNR No — task created from PNR activity
    SeqNo_Charges       int,                             -- SeqNo of Charges table (online payment)
    SeqNo_AcctNotes     int,                             -- SeqNo of AcctNotes (AccountNotes/ClientNotes)
    Itinerary_No        int,                             -- (90000345) Itinerary No. Note: stored in BkgNo column in some tables.

    DoneInd             bit DEFAULT ((0)) NOT NULL,      -- True when task completed
    DoneBy              nvarchar(10),                    -- (JOEB) Completed by user code
    DoneOn              datetime,                        -- Completed date + time

    FrzInd              bit DEFAULT ((0)),               -- Soft delete
    CreatedBy           nvarchar(10) NOT NULL,           -- (JANEB)
    CreatedOn           datetime     NOT NULL,
    UpdatedBy           nvarchar(10) NOT NULL,           -- (JOEB)
    UpdatedOn           datetime     NOT NULL,
    UpdatedAt           nvarchar(20) NOT NULL,           -- IP address of calling system

    CONSTRAINT PK_ToDo PRIMARY KEY (SeqNo)
);
```

### Field Notes

- **`EstJobTime`** — stored as `datetime` in SQL but represents a duration. JSON carries it as a string `"HH:MM"`. The API layer converts.
- **`Accountcode_Client`** and **`SupplierCode`** — leading zeros are significant. Treat as strings.
- **`Brochure_Code_Short`** is also known as `TourSeriesCode`. Use `tour_series_code` in JSON.
- **`Itinerary_No`** — in some legacy tables, `BkgNo` column holds the itinerary number. This is a known schema quirk.
- **Task-source fields** (`travel_pnr_no`, `seq_no_charges`, `seq_no_acct_notes`, `itinerary_no`) are **immutable after insert** — no update allowed.

### Name Anomalies (legacy database)

| DB column            | JSON wire name          |
|----------------------|-------------------------|
| `SeqNo`              | `seq_no`                |
| `Accountcode_Client` | `account_code_client`   |
| `Brochure_Code_Short`| `tour_series_code`      |

---

## Validations

Lookup validations (422 if present but fails lookup):

| Field                               | Lookup query                                                                                  |
|-------------------------------------|-----------------------------------------------------------------------------------------------|
| `JobCode`                           | `SELECT * FROM sales97.dbo.jobs WHERE code = @JobCode`                                        |
| `AssignedToUserCode`, `AssignedByUserCode`, `DoneBy` | `SELECT * FROM sales97.dbo.users WHERE code = @UserCode`               |
| `PriorityCode`                      | `SELECT * FROM sales97.dbo.Priority WHERE code = @PriorityCode`                               |
| `BkgNo`                             | `SELECT * FROM fit.dbo.bookingdetail WHERE bkgno = @BkgNo`                                    |
| `QuoteNo`                           | `SELECT * FROM fit.dbo.fitquote WHERE bkgno = @QuoteNo`                                       |
| `CampaignCode`                      | `SELECT * FROM sales97.dbo.products WHERE ProductCode = @CampaignCode`                        |
| `Accountcode_Client`                | `SELECT * FROM sales97.dbo.accountmast WHERE accountcode = @Accountcode_Client`               |
| `Brochure_Code_Short` (tour_series_code) | `SELECT * FROM brochure.dbo.brochure WHERE Brochure_Code_Short = @Brochure_Code_Short`   |
| `DepDate`                           | `SELECT * FROM brochure.dbo.validdates WHERE Brochure_Code_Short = @Brochure_Code_Short AND validdate = @DepDate` |
| `SupplierCode`                      | `SELECT * FROM sales97.dbo.accountmast WHERE accountcode = @SupplierCode` (same table as clients — distinguished by `AccountType`: IN=client, SU=supplier, TA=travel agent) |
| `Travel_PNRNo`                      | `SELECT * FROM fit.dbo.TravelPNRDeadline WHERE GDSRecordLocator = @Travel_PNRNo`             |
| `SeqNo_Charges`                     | `SELECT * FROM fit.dbo.charges WHERE SeqNo = @SeqNo_Charges`                                 |
| `Itinerary_No`                      | `SELECT * FROM fit.dbo.bkgitinerary WHERE BkgNo = @Itinerary_No`                             |

**Validation response rules:**
- `400` — missing or malformed required fields. List all failing fields.
- `422` — field present but fails business/lookup validation. List all failing fields.

Insert/update writes all audit fields: `CreatedBy`, `CreatedOn`, `UpdatedBy`, `UpdatedOn`, `UpdatedAt`.

---

## Pre-Edit Get Endpoints

These return full record detail for editing. They do **not** filter on `frz_ind` — return the record regardless of frozen state.

| Route | POST body filter fields |
|---|---|
| `POST /api/v1/todos/by-seq-no` | `seq_no` |
| `POST /api/v1/todos/by-booking` | `bkg_no`, `job_code`, `done_ind = false` |
| `POST /api/v1/todos/by-quote` | `quote_no`, `job_code`, `done_ind = false` |
| `POST /api/v1/todos/by-tourseries-departure` | `tour_series_code`, `dep_date`, `job_code`, `done_ind = false` |
| `POST /api/v1/todos/by-task-source` | Exactly one of: `travel_pnr_no`, `seq_no_charges`, `seq_no_acct_notes`, `itinerary_no`. Plus `done_ind = false`. Return `400` if zero or more than one populated. |

The request object used for insert/update may contain only the fields being changed plus the fields above to uniquely identify the record.

---

## Upsert / Create Logic

When a caller POSTs to Create and a task-source field is present:

| Scenario | Behaviour | HTTP code |
|---|---|---|
| Existing open record found (`done_ind = 0`) AND `remark` sent is already contained in DB `remark` | Return no body | `204` |
| Existing open record found AND `remark` sent is not in DB `remark` | Append remark (`db_remark + '; ' + new_remark`), update any differing non-task-source fields, return `seq_no` | `201` |
| Existing open record found AND no `remark` sent | Update any differing non-task-source fields, return `seq_no` | `200` |
| No existing open record — new record created | Return `seq_no` | `201` |

If caller also sends fields that differ from the existing record beyond `remark` (e.g. `due_date`, `assigned_to_user_code`) — those are updated too.

Task-source fields (`travel_pnr_no`, `seq_no_charges`, `seq_no_acct_notes`, `itinerary_no`) are **immutable after insert** — never updated.

---

## List Endpoints

All list queries filter `frz_ind = 0` by default. An optional `include_frozen` boolean (default `false`) removes this filter when `true`. Rights-controlled.

All list endpoints support pagination: include `page_no` and `page_size` in the request. The API fetches `page_size + 1` records; if the count equals `page_size + 1`, set `has_next_page: true` and discard the last record.

| Route | Required / key filter fields | Optional filter fields |
|---|---|---|
| `POST /api/v1/todos/list/by-assignee` | `assigned_to_user_code` | `done_ind` (yes/no/both), `due_date` range |
| `POST /api/v1/todos/list/by-tourseries-departure` | `tour_series_code`, `dep_date` (range) | `done_ind`, `due_date` range |
| `POST /api/v1/todos/list/by-booking` | `bkg_no` | `done_ind` |
| `POST /api/v1/todos/list/by-quote` | `quote_no` | `done_ind` |
| `POST /api/v1/todos/list/by-campaign` | `campaign_code` | `done_ind` |
| `POST /api/v1/todos/list/by-client` | `account_code_client` | `done_ind`, `due_date` range |
| `POST /api/v1/todos/list/by-supplier` | `supplier_code` | `done_ind`, `due_date` range |
| `POST /api/v1/todos/list/by-task-source` | Exactly one of: `travel_pnr_no`, `seq_no_charges`, `seq_no_acct_notes`, `itinerary_no`. Return `400` if zero or more than one populated. | `done_ind`, `due_date` range |

### List Response Shape

Each item in the list contains a summary subset of the record plus joined descriptions:

**Record fields:** `seq_no`, `priority_code`, `start_date`, `due_date`, `assigned_to_user_code`, `task_detail`, `remark`, `created_by`, `created_on`, `updated_by`, `updated_on`, `send_sms_ind`, `send_sms_to`, `sent_mail_ind`, `done_ind`, `account_code_client`, `bkg_no`, `quote_no`, `frz_ind`

**Joined description fields** (to avoid extra round-trips):

| Field | Source |
|---|---|
| `priority_name` | From `PriorityCode` lookup |
| `assigned_to_user_name` | From `AssignedToUserCode` lookup |
| `created_by_name` | From `CreatedBy` lookup |
| `updated_by_name` | From `UpdatedBy` lookup |
| `client_name` | From `account_code_client` join |
| `tour_code` | From `BkgNo` or `QuoteNo` join |
| `itinerary_name` | From `BkgNo` or `QuoteNo` join |

Use inline query. Include a placeholder comment for the actual join SQL (the query differs across MSSQL, Postgres, and MariaDB/MySQL).

**Pagination envelope:**

```json
{
  "items": [ ... ],
  "page_no": 1,
  "page_size": 50,
  "has_next_page": true
}
```

---

## Aggregate Endpoints

Aggregate queries exclude frozen records (`frz_ind = 0`).

"Today" is calculated per tenant timezone: use `browser_timezone` from the auto-injected context to derive UTC from/to bounds, then filter records using those UTC datetime bounds.

### 1. `POST /api/v1/todos/summary/by-user`

**Required:** `assigned_to_user_code`

Returns counts for the given assignee:

| Metric | Condition |
|---|---|
| Tasks due today, split by priority (nested) | `done_ind = 0`, due date falls within today (UTC bounds) |
| Overdue tasks, split by priority (nested) | `done_ind = 0`, due date before today |
| WIP tasks | `done_ind = 0` and `start_date` is set |
| Tasks due today and created today | `done_ind = 0`, due date = today, `created_on` = today |
| Completed today | `done_ind = 1`, `done_on` falls within today |

Receives auto-injected context fields.

### 2. `POST /api/v1/todos/summary/by-context`

**Filter:** exactly one group must be populated — `booking_no`, `quote_no`, `account_code_client`, `supplier_code`, or (`tour_series_code` + `dep_date`). Return `400` if zero or more than one group is populated.

Returns counts for the given context:

| Metric | Condition |
|---|---|
| Tasks due today, split by priority (nested) | `done_ind = 0`, due date falls within today (UTC bounds) |
| Overdue tasks, split by priority (nested) | `done_ind = 0`, due date before today |
| WIP tasks | `done_ind = 0` and `start_date` is set |
| Tasks due today and created today | `done_ind = 0`, due date = today, `created_on` = today |
| Completed tasks | `done_ind = 1` — scope varies by filter type (see below) |

**Completed tasks scope by filter type:**
- `booking_no`, `quote_no`, `tour_series_code + dep_date`: all completed tasks (all time)
- `account_code_client`: completed count for all open enquiries, open quotes, and bookings up to 15 days after return date (15-day window from config)
- `supplier_code`: completed count for the last 30 days (30-day window from config)

Add a placeholder comment in the query for the `account_code_client` and `supplier_code` inline queries — these will be written manually.

Receives auto-injected context fields.

---

## Mutation Endpoints

### Standard mutation responses (unless stated otherwise)

| HTTP code | Meaning |
|---|---|
| `200` | Success |
| `204` | No-op (submitted data identical to DB — no update performed) |
| `404` | Record not found |
| `409` | Concurrency conflict |
| `400` | Missing or malformed required fields |
| `422` | Field present but fails business/lookup validation |

### Concurrency Check

`updated_on` is required on all mutation requests. Concurrency behaviour is controlled by `opsettings.json` `ConcurrencyCheck.StrictMode` (hot-reloadable, per service — not caller-controlled):

- If `StrictMode = true` and the `updated_on` in the DB is later than the value sent → return `409` with body:
  ```json
  { "message": "Record was updated between your read and update. Refresh data." }
  ```

### `PATCH /api/v1/todos/{seq_no}` — Update

Partial update. Send only the fields being changed plus `updated_on` for concurrency. Do not expect all columns. If fetched data is identical to submitted data, return `204` (no update performed).

Response `200`: `{ "seq_no": "1042", "updated_on": "2026-04-03T14:22:00Z" }`

### `POST /api/v1/todos` — Create

See [Upsert / Create Logic](#upsert--create-logic) above for full behaviour including task-source upsert cases.

### `POST /api/v1/todos/{seq_no}/delete` — Hard Delete

```
Body: { "updated_on": "..." }    // concurrency
```

Returns `200` success, `404` not found, `409` concurrency conflict.

### `POST /api/v1/todos/{seq_no}/freeze` — Freeze / Unfreeze

One route for both directions.

```json
{
  "frz_ind": true,        // true = freeze, false = unfreeze
  "updated_on": "..."     // concurrency
}
```

If `freeze` is called with `frz_ind: true` on an already-frozen record, return `422`:
```json
{ "message": "Record is already frozen." }
```

### `POST /api/v1/todos/{seq_no}/complete` — Complete

```json
{
  "done_by": "JOEB",      // mandatory
  "updated_on": "..."     // concurrency
}
```

`done_on` is server-set. If called on a record already marked done, return `422`:
```json
{ "message": "Task is already completed." }
```

### `POST /api/v1/todos/{seq_no}/undo-complete` — Undo Complete

```json
{
  "updated_on": "..."     // concurrency — no domain fields
}
```

Clears `done_by` and `done_on` (set to null). `done_ind` set to `false`.

---

## Rights / Authorisation Placeholder

Include a placeholder comment on each endpoint for rights checks. Users require explicit rights to perform CRUD, freeze, complete, and list operations. The shared project (`Nova.Shared`) handles JWT validation; domain-level rights checks will be added during dotnet code generation.

---

## Sample Data for Mock Server

Use the following sample values when generating mock responses:

| Field | Sample value |
|---|---|
| `seq_no` | `"1042"` |
| `job_code` | `"WH"` |
| `task_detail` | `"Welcome home call"` |
| `assigned_to_user_code` | `"JOEB"` |
| `priority_code` | `"N"` |
| `priority_name` | `"Normal"` |
| `assigned_to_user_name` | `"Joe Blog"` |
| `created_by_name` | `"Jane Blog"` |
| `updated_by_name` | `"Jane Blog"` |
| `client_name` | `"Sunil Gavaskar"` |
| `tour_code` | `"EXP2026"` |
| `itinerary_name` | `"Indian Experience"` |
| `bkg_no` | `500310` |
| `due_date` | `"2026-10-01"` |
| `est_job_time` | `"01:30"` |
| `travel_pnr_no` | `"XY2RP6"` |
| `account_code_client` | `"0000123456"` |
| `tour_series_code` | `"EXP2026"` |
| `dep_date` | `"2026-12-15"` |
| `supplier_code` | `"0000000001"` |
| `tenant_id` | `"T001"` |
| `company_id` | `"C01"` |
| `branch_id` | `"B01"` |
| `user_id` | `"JOEB"` |
| `browser_locale` | `"en-GB"` |
| `browser_timezone` | `"Europe/London"` |
| `ip_address` | `"192.168.1.100"` |

For aggregate counts: use random integers between 0 and 25.

---

## Endpoint Summary

| Method | Path | Purpose |
|---|---|---|
| POST | `/api/v1/todos/by-seq-no` | Pre-edit get by SeqNo |
| POST | `/api/v1/todos/by-booking` | Pre-edit get by BkgNo + JobCode |
| POST | `/api/v1/todos/by-quote` | Pre-edit get by QuoteNo + JobCode |
| POST | `/api/v1/todos/by-tourseries-departure` | Pre-edit get by TourSeriesCode + DepDate + JobCode |
| POST | `/api/v1/todos/by-task-source` | Pre-edit get by task-source (exactly one field) |
| POST | `/api/v1/todos` | Create (with upsert logic for task-source) |
| PATCH | `/api/v1/todos/{seq_no}` | Partial update |
| POST | `/api/v1/todos/{seq_no}/delete` | Hard delete |
| POST | `/api/v1/todos/{seq_no}/freeze` | Freeze / Unfreeze |
| POST | `/api/v1/todos/{seq_no}/complete` | Mark complete |
| POST | `/api/v1/todos/{seq_no}/undo-complete` | Undo complete |
| POST | `/api/v1/todos/list/by-assignee` | List by assigned user |
| POST | `/api/v1/todos/list/by-tourseries-departure` | List by tour series + departure |
| POST | `/api/v1/todos/list/by-booking` | List by booking |
| POST | `/api/v1/todos/list/by-quote` | List by quote |
| POST | `/api/v1/todos/list/by-campaign` | List by campaign |
| POST | `/api/v1/todos/list/by-client` | List by client |
| POST | `/api/v1/todos/list/by-supplier` | List by supplier |
| POST | `/api/v1/todos/list/by-task-source` | List by task-source (exactly one field) |
| POST | `/api/v1/todos/summary/by-user` | Aggregate counts by assignee |
| POST | `/api/v1/todos/summary/by-context` | Aggregate counts by context |
