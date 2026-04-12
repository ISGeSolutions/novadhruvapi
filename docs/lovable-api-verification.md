# Nova API — Lovable Integration Verification Checklist

Use this document whenever the Lovable-generated frontend app produces API calls,
a Postman collection, or HTTP client code that consumes a Nova backend service.

Work through each section in order. Every item must pass before the integration
is considered correct. Failed items identify exactly what to fix in the Lovable
output — do not patch the Nova backend to match incorrect Lovable behaviour.

---

## 1. Wire Format — Request Fields

All field names on the wire are **snake_case**. CamelCase field names will silently
fail to bind on the server — the field is treated as absent, not as an error.

| Check | Pass condition |
|---|---|
| All request body field names are snake_case | `tenant_id` ✓ `tenantId` ✗ |
| All query string parameter names are snake_case | `page_no` ✓ `pageNo` ✗ |
| No field names use PascalCase | — |

**How to verify:** Send a request with a known required field renamed to camelCase.
The server should return `400 Bad Request` with a validation error for that field.
If it returns `200`, the field is optional and the test proves nothing — find a required field.

---

## 2. Wire Format — Response Fields

Nova responses are also snake_case. If the Lovable app reads response fields using
camelCase accessors, the values will be `undefined` silently.

| Check | Pass condition |
|---|---|
| Response fields accessed as snake_case | `body.tenant_id` ✓ `body.tenantId` ✗ |
| Date-only fields read as strings (`"2026-08-15"`) | No JS `Date` parse attempted |
| DateTime fields carry UTC offset (`+00:00` or `Z`) | Both are valid — no exact string match |

---

## 3. RequestContext — Mandatory Body Fields

Every `POST` and `PATCH` request body **must** include all seven `RequestContext` fields.
These are injected by the Nova frontend `apiClient` — if Lovable bypasses the shared
client and constructs requests manually, these fields must be added explicitly.

```json
{
  "tenant_id":        "BTDK",
  "company_id":       "BLX",
  "branch_id":        "HQ",
  "user_id":          "JD",
  "browser_locale":   "en-GB",
  "browser_timezone": "Europe/London",
  "ip_address":       "127.0.0.1"
}
```

| Check | Pass condition |
|---|---|
| `tenant_id` present and non-empty | Required |
| `company_id` present and non-empty | Required |
| `branch_id` present and non-empty | Required |
| `user_id` present and non-empty | Required |
| `browser_locale` present | Required (e.g. `"en-GB"`) |
| `browser_timezone` present | Required (e.g. `"Europe/London"`) |
| `ip_address` present | Required — `"127.0.0.1"` is acceptable for local dev |

**Missing any of these returns `400 Bad Request`.**

---

## 4. JWT Authentication

| Check | Pass condition |
|---|---|
| Every authenticated request includes `Authorization: Bearer <token>` | No token → `401` |
| Token `aud` claim equals `nova-api` | Wrong audience → `401` |
| Token `iss` claim matches `Jwt.Issuer` in the service config | Wrong issuer → `401` |
| Token contains `tenant_id` claim | Missing claim → `403` |
| `tenant_id` claim matches `tenant_id` in the request body | Mismatch → `403` |
| Token is not expired | Expired token → `401` |

**Tenant mismatch (`403`):** A user authenticated as `BTDK` who sends `tenant_id: "other"`
in the body receives `403 Forbidden`, not `401`. Lovable must handle both `401` and `403`
separately — `401` means re-authenticate, `403` means wrong tenant context.

---

## 5. Error Response Format — RFC 9457 Problem Details

All Nova error responses use Problem Details format with
`Content-Type: application/problem+json`.

```json
{
  "type":   "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title":  "Bad Request",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "errors": [
    { "field": "seq_no", "message": "seq_no is required." }
  ]
}
```

| Check | Pass condition |
|---|---|
| Error message read from `detail`, not `message` | `body.detail` ✓ `body.message` ✗ |
| Field-level errors read from `errors[]` array | `body.errors[0].field`, `body.errors[0].message` |
| HTTP client accepts `Content-Type: application/problem+json` | Must not throw on this content type |
| `401`, `403`, `404`, `409`, `422`, `429` all handled | No unhandled status codes |
| `500` handled gracefully (show generic message, do not display `detail`) | — |

---

## 6. Endpoint Routes & Versioning

| Check | Pass condition |
|---|---|
| Business endpoints use `/api/v1/` prefix | `/api/v1/todos` ✓ `/todos` ✗ |
| Health and diagnostic endpoints are unversioned | `/health` ✓ `/api/v1/health` ✗ |
| HTTP method matches the Nova contract | POST for all data retrieval, not GET |
| Route parameters are correctly placed | `/api/v1/todos/{seq_no}` — seq_no in path, not body |

**POST for retrieval:** Nova uses `POST` for all endpoints that accept a request body,
including reads. A Lovable-generated `GET /api/v1/todos/list` will return `404` or `405`
— it must be `POST /api/v1/todos/list/by-assignee` with a body.

---

## 7. Correlation ID — Logging Support

Every Nova response includes `X-Correlation-ID` in the response headers.

| Check | Pass condition |
|---|---|
| Lovable captures and logs `X-Correlation-ID` from error responses | Aids backend log tracing |
| Error reports to the backend team include the correlation ID | Enables root-cause lookup in server logs |

This is not a functional requirement — the API works without it — but it significantly
reduces debugging time when an error is reported.

---

## 8. Cross-Check Against the Canonical Postman Collection

The canonical Postman collections in `postman/` are the source of truth for each service's
contract. Lovable-generated collections must be verified against them.

**Verification steps:**

1. Open the Lovable-generated collection alongside the canonical `postman/Nova.<Service>.Api.postman_collection.json`.
2. For each endpoint the Lovable app calls, find the matching request in the canonical collection.
3. Compare:
   - **URL** — exact path including version prefix
   - **Method** — must match exactly
   - **Request body** — all fields present, snake_case, correct types
   - **Auth** — Bearer token on every authenticated endpoint
   - **RequestContext fields** — all seven present in every POST body
4. Compare the **response handling** in Lovable against the saved examples in the canonical collection — especially error cases.

If the Lovable collection has a request that is **not** in the canonical collection,
it is calling a non-existent endpoint. Do not add that endpoint to Nova — update the
Lovable app to call the correct route.

---

## 9. Service-Specific Notes

### Nova.ToDo.Api

See `src/services/Nova.ToDo.Api/docs/todo-api-amends-for-lovable.md` for a detailed
list of contract changes that Lovable must apply when the backend contract is updated.
This document is updated each time a breaking change is made to the ToDo API contract.

When the Lovable app is updated, re-run this checklist against the new generated output
before merging — backend changes can invalidate previously passing checks.

---

## 10. Quick Smoke Test Sequence

Run these in order against a locally running service to confirm basic integration health.
All use the canonical Postman collection and `Nova — Local Dev` environment.

1. `GET /health` — confirm service is running (`200` or `503`, never `500`)
2. Authenticated endpoint without token — confirm `401`
3. Authenticated endpoint with valid token but wrong `tenant_id` in body — confirm `403`
4. Valid request missing a required RequestContext field — confirm `400` with `errors[]`
5. Valid request, all fields correct — confirm expected `2xx` with snake_case response body
6. Check `X-Correlation-ID` header is present on the response
