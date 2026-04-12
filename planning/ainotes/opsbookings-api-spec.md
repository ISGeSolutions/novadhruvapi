# Nova.OpsBookings.Api — API Specification

**Port:** 5105  
**Purpose:** Operations booking management — traffic light summaries, paginated booking rows, legacy full booking list, and passenger detail saves with colour delta computation.  
**Dependencies:** Nova.CommonUX.Api (auth token).

---

## Common Rules

- All endpoints `POST` (reads) or `PATCH` (writes)
- URL path parameters used for PATCH (`booking_no`, `passenger_id`)
- All request bodies include standard RequestContext: `tenant_id`, `company_id`, `branch_id`, `user_id`, `browser_locale`, `browser_timezone`, `ip_address`
- Wire format: snake_case JSON
- Error format: RFC 9457 ProblemDetails
- All date filter fields are optional unless noted

---

## Endpoints

### Bookings

---

#### POST /api/v1/booking-summary

**Purpose:** Fetch aggregate traffic light counts for bookings in a date range. Used for the ops dashboard KPI tiles.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| branch_id | string | no | Filter by branch; if omitted returns all branches |
| from_date | date | no | ISO 8601 YYYY-MM-DD; must be ≤ to_date |
| to_date | date | no | ISO 8601 YYYY-MM-DD |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{
  "total_bookings": 42,
  "total_clients": 87,
  "bookings": { "green": 20, "amber": 15, "red": 7 },
  "clients": { "green": 50, "amber": 22, "red": 15 }
}
```

`bookings` and `clients` are independent traffic light counts — a booking can be amber while its clients are split red/green.

**Error cases:** 401, 403, 422 (invalid dates or to_date < from_date), 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql aggregate | stored-proc]  
**Tables / SPs:** [NEEDS INPUT: which table(s) hold booking colour/status? Are colours pre-computed columns or computed at query time?]  
**Business logic notes:** [NEEDS INPUT: how is booking colour determined — is it the worst passenger colour? What date field does from_date/to_date filter against (dep_date)?]

---

#### POST /api/v1/booking-rows

**Purpose:** Fetch paginated booking-level rows for the ops grid. Each row has one entry per passenger with per-section traffic light statuses.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| page | integer | yes | Positive integer |
| page_size | integer | yes | One of: 100, 200, 500 |
| branch_id | string | no | |
| from_date | date | no | ISO 8601 |
| to_date | date | no | ISO 8601; must be ≥ from_date if provided |
| ignore_green | boolean | no | If true, exclude rows where all_section = "green" |
| sort_key | string | no | e.g. `"dep_date"` |
| sort_dir | string | no | `"asc"` or `"desc"` |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{
  "rows": [
    {
      "id": "BK-10001-P1",
      "booking_no": "BK-10001",
      "client_id": "CL00010001",
      "client_name": "Mr James Wilson",
      "is_lead": true,
      "dep_date": "2026-03-25",
      "all_section": "green",
      "client_name_status": "green",
      "passport_status": "amber"
    }
  ],
  "total_rows": 87, "current_page": 1, "total_pages": 1, "page_size": 100
}
```

Row ID format: `{booking_no}-{passenger_id}`. `is_lead` = true for the lead passenger.

Status fields (all traffic light strings `"green"` | `"amber"` | `"red"`):
- `all_section` — overall row status (worst of all sections)
- `client_name_status` — name completeness
- `passport_status` — passport details completeness
- [NEEDS INPUT: what other per-section status columns exist?]

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql with pagination | stored-proc — likely SP given the complexity of status computation]  
**Tables / SPs:** [NEEDS INPUT: bookings, passengers/booking_passengers, and status columns]  
**Business logic notes:** [NEEDS INPUT: are status columns pre-computed and stored, or computed on read? What are ALL the section status columns (passport_status, client_name_status, dob_status, tc_status, meals_status, emergency_contact_status, lead_client_contact_status, travel_insurance_status — as seen in Save Booking Client response)?]

---

#### POST /api/v1/bookings

**Purpose:** Fetch the full booking list with nested passenger arrays. **Legacy endpoint** — prefer `booking-summary` + `booking-rows` for large datasets.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| branch_id | string | no | |
| from_date | date | no | ISO 8601 |
| to_date | date | no | ISO 8601; must be ≥ from_date if provided |
| + standard RequestContext fields | | yes | |

**Response (200):** Array of booking objects with nested `passengers` array.
```json
[
  {
    "booking_no": "BK-10001",
    "dep_date": "2026-03-25",
    "return_date": "2026-04-05",
    "tour_name": "Golden Triangle India",
    "tour_code": "GTI-26",
    "sales_consultant": "Alice Cooper",
    "ops_executive": "Bob Marsh",
    "branch_id": "LON",
    "external_url": "https://crm.example.com/bookings/BK-10001",
    "passengers": []
  }
]
```

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql | stored-proc — likely requires JOIN across bookings + passengers]  
**Tables / SPs:** [NEEDS INPUT: bookings, passengers/booking_passengers — and what does `external_url` come from? Is it a constructed URL or stored?]  
**Business logic notes:** [NEEDS INPUT: what fields are included in each passenger object? This endpoint is described as "legacy" — is it being phased out in favour of booking-rows?]

---

#### PATCH /api/v1/bookings/{booking_no}/passengers/{passenger_id}

**Purpose:** Save booking client/passenger details. Returns colour deltas (changes in traffic light counts) and per-section statuses after save — enables the UI to update its counters without a full reload.  
**Auth required:** Yes.

**URL parameters:**
| Parameter | Notes |
|---|---|
| booking_no | e.g. `BK-10001` |
| passenger_id | e.g. `P1` |

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| title | string | no | e.g. `"Mrs"` |
| first_name | string | yes | |
| client_id | string | yes | |
| passport.number | string | no | |
| passport.expiry | date | no | ISO 8601; must be in the future |
| meals.preference | string | no | |
| meals.notes | string | no | |
| current_booking_colour | string | yes | One of: `"green"`, `"amber"`, `"red"` — the client's known current colour before save (for delta computation) |
| current_client_colour | string | yes | Same as above but for client-level colour |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{
  "success": true,
  "booking_colour_delta": { "green": 0, "amber": 1, "red": -1 },
  "client_colour_delta": { "green": 1, "amber": 0, "red": -1 },
  "section_statuses": {
    "all_section": "green",
    "client_name_status": "green",
    "dob_status": "green",
    "tc_status": "amber",
    "passport_status": "green",
    "meals_status": "green",
    "emergency_contact_status": "green",
    "lead_client_contact_status": "green",
    "travel_insurance_status": "red"
  }
}
```

`colour_delta` values are signed integers (positive = increase in that colour, negative = decrease).

**Error cases:** 401, 403, 404 (booking or passenger not found), 422, 429, 500

**DB access pattern:** [NEEDS INPUT: mixed — multiple table writes + status recomputation — likely stored-proc]  
**Tables / SPs:** [NEEDS INPUT: which tables store each section's data? passport table? meals table? tc table? emergency_contact table?]  
**Business logic notes:** [NEEDS INPUT: this is the most complex endpoint. The status recomputation logic (what makes passport_status green vs red) needs to be documented per section. The colour delta computation uses current_booking_colour and current_client_colour as the "before" state to compute the signed delta — is this server-computed or trust the client's claim?]
