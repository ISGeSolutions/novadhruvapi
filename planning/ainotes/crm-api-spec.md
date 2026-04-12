# Nova.CRM.Api — API Specification

**Port:** 5104  
**Purpose:** Client data management — data quality cleansing, client record editing, booking/client search, and transactional email dispatch.  
**Dependencies:** Nova.CommonUX.Api (auth token).

---

## Common Rules

- All endpoints `POST` (reads) or `PATCH` (writes)
- All request bodies include standard RequestContext: `tenant_id`, `company_id`, `branch_id`, `user_id`, `browser_locale`, `browser_timezone`, `ip_address`
- Wire format: snake_case JSON
- Error format: RFC 9457 ProblemDetails

---

## ⚠️ Design Issue — Two PATCH endpoints at `/api/v1/client-records`

The collection defines two distinct PATCH operations at the same URL:
1. **Save Client Records** — batch field edits via `edits[]` array
2. **Save Sheet Details** — single record sheet updates via `seq_no` + `sheet_updates` object

These need to be differentiated. Options:
- [ ] Keep same URL, discriminate by presence of `edits` vs `seq_no` field (fragile)
- [ ] Split into `/api/v1/client-records/batch` and `/api/v1/client-records/sheet`
- [ ] Use a different URL for sheet details, e.g. `/api/v1/client-records/{seq_no}/sheet`

**[NEEDS INPUT: confirm intended URL design before code generation]**

---

## Endpoints

### Client Data

---

#### POST /api/v1/cleansing-options

**Purpose:** Fetch the list of available data cleansing options. Used to populate the cleansing module's option selector.  
**Auth required:** Yes.

**Request body:** Standard RequestContext fields only.

**Response (200):** Array of cleansing option objects.
```json
[
  { "id": "country_cleanup", "label": "Address: Country cleanup", "category": "Data Cleansing" },
  { "id": "postcode_format", "label": "Address: Postcode format", "category": "Data Cleansing" }
]
```

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql | static config | tenant-configurable]  
**Tables / SPs:** [NEEDS INPUT: e.g. cleansing_options]  
**Business logic notes:** [NEEDS INPUT: are these options tenant-configurable or platform-wide?]

---

#### POST /api/v1/client-records

**Purpose:** Fetch paginated client records for a given cleansing option. Each row includes cleansing-relevant fields and a `quality` traffic light.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| option_id | string | yes | Must be a valid cleansing option ID |
| page | integer | yes | Positive integer |
| page_size | integer | yes | One of: 100, 200, 500 |
| sort_key | string | no | e.g. `"Surname"` |
| sort_dir | string | no | `"asc"` or `"desc"` |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{
  "records": [
    {
      "seq_no": 1, "client_id": "CL00003000", "h_hld_seq_no": "HH001000",
      "title": "Mr", "first_name": "James", "middle_name": "", "surname": "Wilson",
      "known_as": "Jim", "country": "UK", "county_state": "London",
      "postcode_zip": "SW1A 1AA", "quality": "amber", "reviewed": false
    }
  ],
  "total_pages": 3, "current_page": 1, "total_records": 150
}
```

`quality`: `"green"` | `"amber"` | `"red"` — computed by the query or SP.

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql | stored-proc]  
**Tables / SPs:** [NEEDS INPUT: which table(s) contain client address/name data? Is quality computed in SQL or application layer?]  
**Business logic notes:** [NEEDS INPUT: does option_id drive different SQL queries (different columns per option), or the same query filtered differently?]

---

#### PATCH /api/v1/client-records — Batch Field Edits

**Purpose:** Save a batch of field-level edits to client records. Each edit includes the full change history (old + new value) for audit trail.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| edits | array | yes | Non-empty array of edit objects |
| edits[].seq_no | integer | yes | |
| edits[].client_id | string | yes | |
| edits[].household_id | string | yes | |
| edits[].changes | array | yes | Non-empty array of `{ field, old_value, new_value }` |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{ "success": true }
```

**Error cases:** 401, 403, 404, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql batch UPDATE | stored-proc]  
**Tables / SPs:** [NEEDS INPUT: which client tables are updated? Is there an audit/history table written simultaneously?]  
**Business logic notes:** [NEEDS INPUT: is this a single transaction across all edits? Partial success allowed?]

---

#### PATCH /api/v1/client-records — Sheet Details

**⚠️ URL CONFLICT — see design issue above.**

**Purpose:** Save Passport or Meals/Allergies sheet details for a single client record. Returns recomputed traffic light statuses for the affected columns.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| seq_no | integer | yes | Positive integer |
| sheet_updates | object | yes | Non-empty — field name → new value map |
| sheet_updates.passport_no | string | no | |
| sheet_updates.passport_expiry | date | no | ISO 8601 YYYY-MM-DD |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{ "success": true, "statuses": { "passport_status": "green" } }
```

`statuses` is a partial map — only includes statuses for the fields that were updated.

**Error cases:** 401, 403, 404, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql | stored-proc — mixed likely, since recomputing statuses requires business logic]  
**Tables / SPs:** [NEEDS INPUT: which tables? client_passports? client_meals?]  
**Business logic notes:** [NEEDS INPUT: what is the logic to compute passport_status (green/amber/red)? Based on expiry date vs departure date?]

---

#### POST /api/v1/clients/{client_id}/history

**Purpose:** Fetch the change audit history for a specific client. `client_id` is a URL path parameter.  
**Auth required:** Yes.

**URL parameter:**
| Parameter | Notes |
|---|---|
| client_id | e.g. `CL00010002` |

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| limit | integer | no | 1–500, defaults to [NEEDS INPUT] |
| + standard RequestContext fields | | yes | |

**Response (200):** Array of history entries.
```json
[
  {
    "timestamp": "2026-03-14T09:12:00.000Z",
    "action": "field_updated",
    "changed_by": "user-123",
    "details": { "field": "Country", "old_value": "UK", "new_value": "United Kingdom" }
  }
]
```

**Error cases:** 401, 403, 404 (client not found), 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql against audit/history table]  
**Tables / SPs:** [NEEDS INPUT: e.g. client_audit_log, client_change_history]  
**Business logic notes:** [NEEDS INPUT: is this the same history table that batch edits write to? What is the default limit if not provided?]

---

### Search

---

#### POST /api/v1/search/clients

**Purpose:** Full-text search for clients by name, email, or phone. Returns a short list of matching records.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| query | string | yes | Search term |
| + standard RequestContext fields | | yes | |

**Response (200):** Array of client matches.
```json
[
  { "client_id": "C001", "name": "John Henderson", "email": "john.h@example.com", "phone": "+44 7700 900123" }
]
```

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql with LIKE/full-text search | stored-proc]  
**Tables / SPs:** [NEEDS INPUT: which client table? Is there a search index?]  
**Business logic notes:** [NEEDS INPUT: what fields does `query` search across — name only, or also email + phone? Is there a minimum query length? Result limit?]

---

#### POST /api/v1/search/enquiry-quote-booking-no

**Purpose:** Search for enquiries, quotes, or bookings by reference number. Returns type, status, and client name.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| query | string | yes | Reference number (partial match supported) |
| + standard RequestContext fields | | yes | |

**Response (200):** Array of matching records.
```json
[
  { "type": "booking", "reference_no": "BK-2026-0042", "client_name": "John Henderson", "date": "2026-03-10", "status": "Confirmed" }
]
```

`type`: `"enquiry"` | `"quote"` | `"booking"`

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: UNION query across enquiries/quotes/bookings | stored-proc]  
**Tables / SPs:** [NEEDS INPUT: e.g. enquiries, quotes, bookings tables — likely different DBs or schemas per legacy system]  
**Business logic notes:** [NEEDS INPUT: is this cross-tenant or scoped to tenant_id? Result limit?]

---

### Email

---

#### POST /api/v1/email/missing-info

**Purpose:** Send a templated HTML/plain-text email to a passenger requesting completion of missing booking information sections.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| booking_no | string | yes | |
| passenger_id | string | yes | e.g. `BK-10001-P1` |
| client_id | string | yes | |
| recipient.email | string | yes | Valid email |
| recipient.title | string | no | e.g. `"Mr"` |
| recipient.first_name | string | yes | |
| recipient.surname | string | yes | |
| booking_context.tour_name | string | no | |
| booking_context.tour_code | string | no | |
| booking_context.dep_date | date | no | ISO 8601 |
| booking_context.return_date | date | no | ISO 8601 |
| missing_sections | array | yes | Min 1 section; each has `label` (string) + `fields` (string[]) |
| format | string | no | `"html"` (default) or `"plain"` |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{ "success": true, "message_id": "msg_abc123", "sent_to": "james.wilson@example.com" }
```

**Error cases:** 401, 403, 404, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: no DB read needed (all data in request) — email dispatch only]  
**Tables / SPs:** [NEEDS INPUT: is there an outbox/email_log table to write to? Does this use the transactional outbox pattern?]  
**Business logic notes:** [NEEDS INPUT: which email provider? SMTP / SendGrid / AWS SES? Is there a Handlebars/Razor email template? Does browser_locale drive template language selection?]
