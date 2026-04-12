# Nova.Analytics.Api — API Specification

**Port:** 5107  
**Purpose:** Real-time client session observability — active sessions, historical session analysis, page visit tracking, and summary stats. Read-only against session data. Also emits and responds to Socket.IO real-time push events for live dashboard updates.  
**Dependencies:** Nova.CommonUX.Api (auth token).

---

## Common Rules

- All REST endpoints are `POST` — **read-only, no writes**
- All request bodies include standard RequestContext: `tenant_id`, `company_id`, `branch_id`, `user_id`, `browser_locale`, `browser_timezone`, `ip_address`
- Wire format: snake_case JSON
- Error format: RFC 9457 ProblemDetails

---

## ⚠️ Architectural Note — Socket.IO

This service has a significant Socket.IO layer alongside the REST endpoints. The collection documents it as "reference only", but the server must implement:

1. A Socket.IO server endpoint at `/socket.io` (or configurable path)
2. JWT authentication for Socket.IO connections — **[NEEDS INPUT: same JWT? Separate handshake?]**
3. Server-push emission of all events listed below

The REST endpoints return the current state snapshot; Socket.IO events deliver incremental updates. The frontend subscribes to events and applies deltas without polling.

**[NEEDS INPUT: which Socket.IO .NET library? SignalR with Socket.IO adapter? SocketIOSharp? Confirm .NET 10 compatible library.]**

---

## REST Endpoints

### Realtime Client View

---

#### POST /api/v1/real-time-client-view-active-sessions

**Purpose:** Return all currently active client sessions across the tenant.  
**Auth required:** Yes.

**Request body:** Standard RequestContext fields only.

**Response (200):** Array of active session objects.
```json
[
  {
    "id": "sess-001", "booking_no": "BK-12345", "client_name": "John Smith",
    "consultant": "Sarah M", "product": "Safari Adventure", "destination": "Kenya",
    "departure_date": "2026-06-15", "return_date": "2026-06-25", "pax": 2,
    "status": "Active", "last_action": "Viewing itinerary", "last_action_time": "2 min ago",
    "current_page": "Itinerary", "session_duration": "12:34", "idle_time": "00:00",
    "actions_count": 15, "app_code": "NV", "device": "Desktop", "os": "Windows 11",
    "browser": "Chrome 120", "is_agent_booking": false
  }
]
```

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: this is "real-time" — is session data in Redis, an in-memory store, or a DB table? Is it pushed from the client app or polled?]  
**Tables / SPs:** [NEEDS INPUT: sessions table or Redis key pattern?]  
**Business logic notes:** [NEEDS INPUT: what defines "active" vs "idle" vs "closed"? Is last_action_time a computed string ("2 min ago") from the server, or a raw timestamp the client formats?]

---

#### POST /api/v1/real-time-client-view-session-detail

**Purpose:** Return full detail for a single active session including page_views and interactions arrays.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| session_id | string | yes | e.g. `"sess-001"` |
| + standard RequestContext fields | | yes | |

**Response (200):** Single session detail object — all fields from active sessions list plus:
```json
{
  "quote_no": "Q-98765", "total_value": "£4,500", "payment_status": "Deposit Paid",
  "notes": "VIP client", "referral_url": "https://google.com",
  "avatar_url": "", "ip": "192.168.1.50",
  "page_views": [], "interactions": []
}
```

**[NEEDS INPUT: what is the shape of a page_views entry? An interactions entry?]**

**Error cases:** 401, 403, 404 (session not found), 422, 429, 500

**DB access pattern:** [NEEDS INPUT: same session store as active sessions]

---

#### POST /api/v1/real-time-client-view-historical-sessions

**Purpose:** Return paginated historical sessions (completed/abandoned). Supports server-side sorting and multi-field filtering.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| page | integer | yes | Positive integer |
| page_size | integer | yes | e.g. 25 |
| sort_key | string | no | e.g. `"client_name"` |
| sort_dir | string | no | `"asc"` or `"desc"` |
| filters.consultant | string | no | |
| filters.status | string[] | no | e.g. `["Completed", "Abandoned"]` |
| filters.app_code | string | no | |
| filters.departure_date_from | date | no | ISO 8601 |
| filters.departure_date_to | date | no | ISO 8601 |
| + standard RequestContext fields | | yes | |

**Response (200):**
```json
{
  "records": [
    {
      "id": "hist-001", "date_time": "2026-03-15T10:30:00Z",
      "user": "guest", "user_type": "Guest", "client_name": "Jane Doe",
      "consultant": "Priya M", "product": "European Explorer",
      "booking_no": "BK-54321", "quote_no": "Q-11111", "entry_type": "Return Visit",
      "landing_page": "Home", "browser": "Safari 17", "os": "macOS",
      "status": "Completed", "session_start": "10:30", "last_seen": "11:15",
      "session_end": "11:20", "duration": "00:50:00", "app_code": "NV",
      "departure_date": "2026-07-01", "destination": "France", "device": "Desktop",
      "is_agent_booking": false, "actions_count": 22
    }
  ],
  "total_records": 200, "current_page": 1, "total_pages": 8, "page_size": 25
}
```

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql with dynamic filters and pagination | stored-proc]  
**Tables / SPs:** [NEEDS INPUT: session_history or similar archive table]  
**Business logic notes:** [NEEDS INPUT: are historical sessions archived from the active sessions store? What triggers archival (session close)?]

---

#### POST /api/v1/real-time-client-view-historical-detail

**Purpose:** Return full detail for a single historical session.  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| session_id | string | yes | e.g. `"hist-001"` |
| + standard RequestContext fields | | yes | |

**Response (200):** Single historical session detail — all list fields plus:
```json
{
  "client_id": "CLI-001", "session_id": "SESS-001", "landing_url": "https://example.com",
  "referrer": "https://google.com", "referral_url": "https://google.com", "ip": "10.0.0.5",
  "page_views": [], "interactions": []
}
```

**Error cases:** 401, 403, 404, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: same as historical sessions but single-row with page_views/interactions joined]

---

#### POST /api/v1/real-time-client-view-page-visits

**Purpose:** Return the page visit history for a given session (active or historical).  
**Auth required:** Yes.

**Request body:**
| Field | Type | Required | Notes |
|---|---|---|---|
| session_id | string | yes | |
| + standard RequestContext fields | | yes | |

**Response (200):** Array of page visit objects.
```json
[
  {
    "page": "Home", "url": "https://example.com/", "time_on_page": "02:15",
    "timestamp": "2026-03-15T10:30:00Z", "visit_date": "2026-03-15",
    "device": "Desktop", "os": "Windows 11", "browser": "Chrome 120", "ip": "192.168.1.50"
  }
]
```

**Error cases:** 401, 403, 404, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: inline-sql from page_visits or session_page_views table]  
**Business logic notes:** [NEEDS INPUT: `time_on_page` — computed from timestamps, or stored directly?]

---

#### POST /api/v1/real-time-client-view-summary-stats

**Purpose:** Return aggregated session counts — active, idle, closed, and total. Used for KPI tiles at the top of the analytics dashboard.  
**Auth required:** Yes.

**Request body:** Standard RequestContext fields only.

**Response (200):**
```json
{ "active": 12, "idle": 5, "closed": 28, "total": 45 }
```

**Error cases:** 401, 403, 422, 429, 500

**DB access pattern:** [NEEDS INPUT: COUNT queries against session store / Redis SCARD?]  
**Business logic notes:** [NEEDS INPUT: are these counts computed on demand or maintained as counters (e.g. Redis INCR/DECR)?]

---

## Socket.IO Events (Server-emitted, reference only)

These are push events — no REST request needed. Clients subscribe to the Socket.IO connection.

| Event name | Description | Key payload fields |
|---|---|---|
| `task-counts-updated` | Periodic task KPI update | tasks_high, tasks_normal, tasks_low, overdue_*, tasks_due_today, wip_tasks, next_7_days_tasks[] |
| `new-celebration` | Team celebration event | id, type (new_booking\|milestone\|team_win), message, timestamp |
| `new-enquiries-updated` | New enquiry count changed | new_enquiries (integer) |
| `unclosed-web-enquiries-updated` | Unclosed web enquiry count changed | unclosed_web_enquiries (integer) |
| `avg-enquiry-response-time-updated` | Avg response time changed | avg_response_minutes (integer) |
| `active-users-updated` | Active user list changed | users[] with id, name, initials, avatar_url, status, status_label, status_note, last_seen, team |
| `grouptour-task-activity-updated` | Activity status changed (from OpsGroups.Api) | departure_id, activity_id, template_code, status, notes, updated_by, updated_at |
| `grouptour-task-departure-updated` | Departure-level change (from OpsGroups.Api) | departure_id, fields_changed[], snapshot{}, updated_by, updated_at |
| `grouptour-task-summary-updated` | Ops summary stats changed | overdue, due_later, done_today, done_past |
| `real-time-client-view-sessions-updated` | Session list/counts changed | [NEEDS INPUT: payload shape not fully captured in collection] |

**[NEEDS INPUT: does Analytics.Api own ALL Socket.IO events, or do OpsGroups.Api and other services emit to their own Socket.IO connections? Is there a shared event bus (RabbitMQ) that Analytics.Api listens to and rebroadcasts?]**
