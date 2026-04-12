# Nova.OpsGroups.Api — API Specification

**Port:** 5106  
**Purpose:** Operations group tour task management — departures, per-departure activity tracking (with SLA rules), activity templates, series, destinations, team members, and dashboard summary stats. Also emits Socket.IO real-time events when activities or departures change.  
**Dependencies:** Nova.CommonUX.Api (auth token).

---

## Common Rules

- All endpoints `POST` (reads) or `PATCH` (writes)
- URL path parameters used for resource-scoped operations (`departure_id`, `activity_id`, `template_code`)
- All request bodies include standard RequestContext: `tenant_id`, `company_id`, `branch_id`, `user_id`, `browser_locale`, `browser_timezone`, `ip_address`
- Wire format: snake_case JSON
- Error format: RFC 9457 ProblemDetails
- Activity status enum: `"not_started"` | `"in_progress"` | `"waiting"` | `"complete"` | `"overdue"` | `"na"`

---

## Socket.IO Events (Server-emitted)

This service must emit the following Socket.IO events when data changes:

| Event name | Trigger | Payload summary |
|---|---|---|
| `grouptour-task-activity-updated` | Single activity PATCH | departure_id, activity_id, template_code, status, notes, updated_by, updated_at |
| `grouptour-task-departure-updated` | Departure-level field change | departure_id, fields_changed[], snapshot{}, updated_by, updated_at |
| `grouptour-task-summary-updated` | Any activity/departure change | overdue, due_later, done_today, done_past counts |

**[NEEDS INPUT: which Socket.IO library? SignalR / Socket.IO .NET? Connection auth — same JWT?]**

---

## Endpoints

### Group Tour Tasks

---

#### POST /api/v1/grouptour-task-departures

**Purpose:** Fetch the filtered departure grid with activity status summaries, readiness percentage, and risk level per departure. Server-side pagination.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| status_filter | string | no | `"all"` or specific status value |
| date_from | date | no | ISO 8601 YYYY-MM-DD |
| date_to | date | no | ISO 8601; must be ≥ date_from if both provided |
| series_code | string | no | Filter by series |
| destination_code | string | no | Filter by destination |
| assigned_to | string | no | Filter by ops team member user_id |
| search | string | no | Free text search |
| ops_manager | string | no | Filter by ops manager name/id |
| ops_exec | string | no | Filter by ops executive name/id |
| page | integer | yes | Positive integer |
| page_size | integer | yes | [NEEDS INPUT: allowed values — 100/200/500 or free?] |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{
  "departures": [
    {
      "departure_id": "DEP-001",
      "series_code": "NORFJORD", "series_name": "Norwegian Fjords Explorer",
      "departure_date": "2026-06-15", "return_date": "2026-06-22",
      "destination_code": "NORWAY", "destination_name": "Norway",
      "pax_count": 28, "booking_count": 14,
      "ops_manager": "Sarah M.", "ops_exec": "Tom B.",
      "gtd": false, "notes": "",
      "activities": [
        {
          "activity_id": "ACT-001", "template_code": "VISA",
          "status": "complete", "due_date": "2026-05-15",
          "updated_at": "2026-05-10", "updated_by": "user-1",
          "notes": "", "source": "GLOBAL"
        }
      ],
      "readiness_pct": 75, "risk_level": "amber"
    }
  ],
  "total": 42, "page": 1, "page_size": 100
}
```

`activity.source`: `"GLOBAL"` | `"SERIES"` | `"DESTINATION"` — indicates which SLA rule level generated this activity.  
`risk_level`: `"green"` | `"amber"` | `"red"` — [NEEDS INPUT: computation logic?]  
`readiness_pct`: integer 0–100 — [NEEDS INPUT: computation formula — pct of complete activities?]

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql | stored-proc — likely SP given the complexity of joining departures + activities + SLA rules]  
**Tables / SPs:** [NEEDS INPUT: departures, departure_activities, activity_templates, sla_rules, series, destinations]  
**Business logic notes:** [NEEDS INPUT: how are activities generated per departure — from SLA rules on departure creation, or dynamically at query time? How is readiness_pct computed?]

---

#### POST /api/v1/grouptour-task-departures/{departure_id}

**Purpose:** Fetch a single departure with full activity detail and last_updated timestamp.  
**Auth required:** Yes.

**URL parameter:** `departure_id` (e.g. `DEP-001`)

**Request body:** Standard RequestContext fields only.

**Response (200):** Same structure as a single departure object from the list response, plus `last_updated` (ISO 8601 datetime).

**Error cases:** 401, 403, 404 (departure not found), 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql | stored-proc]  
**Tables / SPs:** [NEEDS INPUT: same as Fetch Departures but single-row]  
**Business logic notes:** [NEEDS INPUT: is this the same query as the list but filtered by departure_id, or a separate deeper query?]

---

#### PATCH /api/v1/grouptour-task-departures/{departure_id}/activities/{activity_id}

**Purpose:** Update a single activity's status, notes, or completion date. Must emit `grouptour-task-activity-updated` Socket.IO event on success.  
**Auth required:** Yes.

**URL parameters:** `departure_id`, `activity_id`

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| status | string | yes | One of the activity status enum values |
| notes | string | no | |
| completed_date | date | no | ISO 8601 YYYY-MM-DD |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{ "activity_id": "ACT-001", "status": "complete", "notes": "All visas confirmed", "completed_date": "2026-03-18", "updated_at": "2026-03-18T14:35:00.000Z" }
```

**Error cases:** 401, 403, 404 (activity not found), 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql UPDATE | stored-proc]  
**Tables / SPs:** [NEEDS INPUT: departure_activities table]  
**Business logic notes:** [NEEDS INPUT: should setting status to "complete" automatically set completed_date = today if not provided? Audit trail required?]

---

#### PATCH /api/v1/grouptour-task-activity-bulk-update

**Purpose:** Bulk update multiple activities across departures in a single request. Each update includes `old_status` for conflict detection and audit trail.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| updates | array | yes | Non-empty array of update objects |
| updates[].departure_id | string | yes | |
| updates[].activity_id | string | yes | |
| updates[].old_status | string | yes | Current status — for conflict detection |
| updates[].new_status | string | yes | Target status |
| updates[].notes | string | no | |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{
  "success": true, "updated_count": 2,
  "results": [
    { "activity_id": "ACT-001", "status": "complete", "updated_at": "2026-03-19T14:35:00.000Z", "updated_by": "U001" },
    { "activity_id": "ACT-003", "status": "waiting", "updated_at": "2026-03-19T14:35:00.000Z", "updated_by": "U001" }
  ]
}
```

**Error cases:** 401, 403, 404, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: batch UPDATE in single transaction | stored-proc]  
**Tables / SPs:** [NEEDS INPUT]  
**Business logic notes:** [NEEDS INPUT: if old_status doesn't match current DB value (conflict), does the whole batch fail, or is the conflicted row skipped with a partial success response? Should each update emit an individual Socket.IO event, or a single summary event?]

---

#### POST /api/v1/grouptour-task-activity-templates

**Purpose:** Fetch all activity template definitions (global and tenant-level).  
**Auth required:** Yes.

**Request body:** Standard RequestContext fields only.

**Response (200):**
```json
{
  "templates": [
    {
      "template_code": "VISA", "template_name": "Visa Check",
      "required": true, "critical": true,
      "sla_offset_days": -30, "reference_date": "departure",
      "source": "GLOBAL", "is_active": true
    }
  ]
}
```

`sla_offset_days`: negative integer — days before `reference_date` the activity is due.  
`reference_date`: `"departure"` | [NEEDS INPUT: other values?]  
`source`: `"GLOBAL"` | [NEEDS INPUT: tenant-level sources?]

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql]  
**Tables / SPs:** [NEEDS INPUT: activity_templates table]  
**Business logic notes:** [NEEDS INPUT: are there both global platform templates and tenant-customisable templates?]

---

#### PATCH /api/v1/grouptour-task-activity-templates/{template_code}

**Purpose:** Update an activity template definition.  
**Auth required:** Yes.

**URL parameter:** `template_code` (e.g. `VISA`)

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| template_name | string | no | |
| required | boolean | no | |
| critical | boolean | no | |
| sla_offset_days | integer | yes | Must be a negative integer |
| reference_date | string | no | e.g. `"departure"` |
| source | string | no | |
| is_active | boolean | no | |
| + standard RequestContext fields | | yes | |

**Response (200):** Updated template object with `updated_at` timestamp.

**Error cases:** 401, 403, 404 (template not found), 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql UPDATE]  
**Tables / SPs:** [NEEDS INPUT]  
**Business logic notes:** [NEEDS INPUT: can global templates be edited by tenants, or only tenant-level ones?]

---

#### POST /api/v1/grouptour-task-series

**Purpose:** Fetch all series (tour product definitions).  
**Auth required:** Yes.

**Request body:** Standard RequestContext fields only.

**Response (200):**
```json
{
  "series": [
    { "series_id": "S001", "series_code": "NORFJORD", "series_name": "Norwegian Fjords Explorer", "destination_code": "NORWAY", "departure_count": 12, "is_active": true }
  ]
}
```

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql]  
**Tables / SPs:** [NEEDS INPUT: series / tour_series table]  
**Business logic notes:** [NEEDS INPUT: is departure_count a stored value or a COUNT subquery?]

---

#### POST /api/v1/grouptour-task-series-import

**Purpose:** Import series definitions from CSV data.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| csv_data | string | yes | CSV string with header row; required columns: `series_code`, `series_name`, `destination_code` |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{ "imported_count": 1, "skipped_count": 0, "errors": [] }
```

**Error cases:** 401, 403, 422 (invalid CSV format or missing columns), 429, 500

**DB access pattern:** [NEEDS INPUT: batch INSERT / UPSERT]  
**Tables / SPs:** [NEEDS INPUT]  
**Business logic notes:** [NEEDS INPUT: upsert on series_code (update if exists)? What happens with duplicates — skip or overwrite?]

---

#### POST /api/v1/grouptour-task-team-members

**Purpose:** Fetch the operations team member list (for assignment dropdowns).  
**Auth required:** Yes.

**Request body:** Standard RequestContext fields only.

**Response (200):**
```json
{
  "members": [
    { "user_id": "user-1", "name": "Sarah Mitchell", "initials": "SM", "email": "sarah@example.com", "role": "ops_manager", "is_active": true }
  ]
}
```

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql]  
**Tables / SPs:** [NEEDS INPUT: users filtered by role = ops_manager / ops_exec?]  
**Business logic notes:** [NEEDS INPUT: are team members a subset of users (by role), or a separate table?]

---

#### POST /api/v1/grouptour-task-sla-rules

**Purpose:** Fetch SLA hierarchy rules. Returns both global rules and series-level overrides.  
**Auth required:** Yes.

**Request body:** Standard RequestContext fields only.

**Response (200):**
```json
{
  "global": [
    { "rule_id": "SLA-001", "level": "global", "activity_code": "VISA", "offset_days": -30, "reference_date": "departure", "required": true, "critical": true }
  ],
  "series_overrides": [
    { "rule_id": "SLA-010", "level": "tour_series", "activity_code": "VISA", "series_code": "NORFJORD", "offset_days": -45, "reference_date": "departure", "required": true, "critical": true }
  ]
}
```

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql — two queries (global + overrides) or single query partitioned by level]  
**Tables / SPs:** [NEEDS INPUT: sla_rules table with level column?]  
**Business logic notes:** [NEEDS INPUT: are there destination-level overrides too (not shown in response — only global + series_overrides)?]

---

#### PATCH /api/v1/grouptour-task-sla-rules

**Purpose:** Save SLA rule overrides at global, series, or destination level.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| rules | array | yes | Non-empty array of rule objects |
| rules[].level | string | yes | e.g. `"tour_series"`, `"global"` |
| rules[].activity_code | string | yes | |
| rules[].offset_days | integer | yes | Must be a negative integer |
| rules[].reference_date | string | yes | |
| rules[].required | boolean | yes | |
| rules[].critical | boolean | yes | |
| rules[].series_code | string | conditional | Required when level = `"tour_series"` |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{ "success": true, "saved_count": 1, "rules": [ { "rule_id": "SLA-010", ... , "updated_at": "..." } ] }
```

**Error cases:** 401, 403, 404, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: UPSERT — insert or update by level + activity_code + series_code]  
**Tables / SPs:** [NEEDS INPUT]  
**Business logic notes:** [NEEDS INPUT: batch in single transaction? Can rules be deleted via this endpoint, or is delete a separate operation?]

---

#### POST /api/v1/grouptour-task-destinations

**Purpose:** Fetch all destination definitions.  
**Auth required:** Yes.

**Request body:** Standard RequestContext fields only.

**Response (200):**
```json
{
  "destinations": [
    { "destination_code": "NORWAY", "destination_name": "Norway", "region": "Scandinavia", "is_active": true }
  ]
}
```

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql]  
**Tables / SPs:** [NEEDS INPUT: destinations table]

---

#### POST /api/v1/grouptour-task-destinations-import

**Purpose:** Import destination definitions from a JSON array.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| destinations | array | yes | Array of `{ destination_code, destination_name, region }` |
| destinations[].destination_code | string | yes | |
| destinations[].destination_name | string | yes | |
| destinations[].region | string | no | |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{ "imported_count": 1, "skipped_count": 0, "errors": [] }
```

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: batch UPSERT on destination_code]  
**Business logic notes:** [NEEDS INPUT: upsert or insert-only? duplicate handling?]

---

#### POST /api/v1/grouptour-task-summary-stats

**Purpose:** Fetch dashboard summary counts for group tour task operations — used for the KPI header tiles.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| date_from | date | no | ISO 8601 |
| date_to | date | no | ISO 8601 |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{
  "total_departures": 42,
  "overdue_activities": 5,
  "due_this_week": 12,
  "completed_today": 3,
  "readiness_avg_pct": 68
}
```

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql aggregates | stored-proc]  
**Tables / SPs:** [NEEDS INPUT: likely same tables as Fetch Departures — aggregate counts]  
**Business logic notes:** [NEEDS INPUT: "due_this_week" and "overdue" are relative to today's date — confirm these are computed on read, not stored]
