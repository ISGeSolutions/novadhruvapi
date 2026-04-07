# Nova.ToDo.Api — API Contract Amends for Lovable

The Nova.ToDo.Api backend contract has been updated. Please amend all API calling code
to match the changes below. Do not change any UI, layout, or business logic —
only the HTTP request/response handling.

---

## 1. Error response format — MOST IMPORTANT

All error responses (4xx and 5xx) now return RFC 9457 Problem Details format
with Content-Type: `application/problem+json`.

**Old format (remove all handling of this):**

```json
{ "message": "Unauthorised. Valid JWT Bearer token required." }
```

```json
{ "errors": [{ "field": "seq_no", "message": "seq_no is required." }] }
```

**New format (handle this everywhere):**

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2",
  "title": "Unauthorized",
  "status": 401,
  "detail": "Unauthorised. Valid JWT Bearer token required."
}
```

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "errors": [
    { "field": "seq_no", "message": "seq_no is required." }
  ]
}
```

**Rules:**

- Read the error message from `detail` (not `message`)
- Read field-level errors from `errors[]` — structure unchanged (field + message)
- Read the HTTP status code from `status` (or from the HTTP response status — they match)
- The Content-Type of error responses is `application/problem+json` — ensure your
  HTTP client does not reject this (treat it the same as `application/json`)

---

## 2. Handle 403 Forbidden

The API can now return 403 in addition to 401. Add handling wherever 401 is currently
handled. 403 means the user is authenticated but lacks permission for that action.
Show an appropriate message rather than redirecting to login.

403 body follows the same Problem Details format:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
  "title": "Forbidden",
  "status": 403,
  "detail": "You do not have permission to perform this action."
}
```

---

## 3. Summary by Context endpoint — field name fix

Endpoint: `POST /api/v1/todos/summary/by-context`

If the booking number filter is being sent as `booking_no`, rename it to `bkg_no`.

**Old:**

```json
{ "booking_no": 500310 }
```

**New:**

```json
{ "bkg_no": 500310 }
```

---

## 4. updated_at field — display fix

The `updated_at` field on ToDo records is **not** a timestamp. It stores the client
IP address of the last update (e.g. `"192.168.1.100"`). If this field is currently
being displayed as a date/time anywhere, remove the date formatting and either
display it as plain text or hide it from the UI.

---

## 5. X-Correlation-ID response header (optional but recommended)

All API responses now include an `X-Correlation-ID` response header
(e.g. `X-Correlation-ID: a1b2c3d4-e5f6-7890-abcd-ef1234567890`).

If your error handling or logging captures response headers, log this value
alongside any error — it allows the backend team to trace the exact request
in the server logs.

---

## No other changes

- All endpoint URLs are unchanged
- All request body shapes are unchanged
- All successful (2xx) response body shapes are unchanged
- Timestamp strings in responses changed from `Z` suffix to `+00:00`
  (e.g. `"2026-04-03T09:00:00Z"` → `"2026-04-03T09:00:00+00:00"`) —
  both are valid ISO 8601 and JavaScript `Date` handles both correctly,
  so no code change is needed unless you are doing exact string matching
