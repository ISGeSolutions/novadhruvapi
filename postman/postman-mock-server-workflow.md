# Postman Mock Server Workflow — Nova API Services

Applies to all Nova API service Postman collections (e.g. Nova.ToDo.Api, Nova.Accounting.Api).

Each service ships a single collection file:

```
postman/Nova.<service-name>.Api.postman_collection.json
```

This file serves two purposes:

1. **API reference** — run against a locally running service (`baseUrl = http://localhost:<port>`)
2. **Mock server** — import into Postman and create a Mock Server for frontend / UX development

No separate mock collection file is maintained. The main collection is mock-ready by design.

---

## Why one file is enough

Postman's Mock Server works directly from saved response examples in a collection. Every request in a Nova collection has:

- Named response examples for every HTTP status code the endpoint can return
- `originalRequest` on each example (required for Postman mock matching)
- `_postman_previewlanguage: json` and `Content-Type` response headers

This means the collection can be used as a mock server without any modifications.

---

## Creating the mock server

### Step 1 — Import the collection

In Postman: **Import** → select `Nova.<service-name>.Api.postman_collection.json`

### Step 2 — Create a Mock Server

Right-click the collection → **Mock Servers** → **Create Mock Server**

- Give the mock a name (e.g. `Nova.<service-name>.Api Mock`)
- Leave matching options at defaults
- Click **Create Mock Server**

Postman generates a URL in the form:

```
https://<generated-id>.mock.pstmn.io
```

### Step 3 — Update baseUrl

In the collection's **Variables** tab, set `baseUrl` to the generated mock URL:

```
https://<generated-id>.mock.pstmn.io
```

The collection is now pointing at the mock server.

---

## Using the mock server

### Happy path (default behaviour)

Send any request without extra headers. Postman returns the **first** saved example for that endpoint — always the `200 OK` (or `201 Created`) success case.

```
POST https://<mock-id>.mock.pstmn.io/api/v1/todos/by-seq-no
Content-Type: application/json

{ "seq_no": "1042", ... }
```

→ Returns the `200 OK` example body.

### Requesting a specific response

To receive a specific named example (e.g. an error scenario), add the `x-mock-response-name` header to your request. The value must match the example name exactly as it appears in the collection.

```
POST https://<mock-id>.mock.pstmn.io/api/v1/todos/by-seq-no
Content-Type: application/json
x-mock-response-name: 404 Not Found

{ "seq_no": "9999", ... }
```

→ Returns the `404 Not Found` example body.

Alternatively, match by HTTP status code (returns the first example with that code):

```
x-mock-response-code: 422
```

### Example names to use

Each endpoint's response examples follow this naming convention:

| Scenario | Header value |
|---|---|
| Success | `200 OK` or `201 Created` (default — no header needed) |
| No changes made | `204 No Content — No Changes` |
| Missing required field | `400 Bad Request — Missing <field>` |
| Unauthenticated | `401 Unauthorised` |
| Not found | `404 Not Found` |
| Concurrency conflict | `409 Concurrency Conflict` |
| Lookup validation failed | `422 Unprocessable Entity — Lookup Validation Failed` |
| Already in terminal state | `422 Unprocessable Entity — <reason>` |

The exact name is visible in Postman under the endpoint's **Examples** tab, and in each endpoint's `description` field.

---

## Switching between mock and real service

The `baseUrl` variable controls the target. Switch as needed:

| Target | baseUrl value |
|---|---|
| Local dev server | `http://localhost:<port>` |
| Postman mock | `https://<generated-id>.mock.pstmn.io` |

Use Postman **Environments** to manage this cleanly across team members without editing the collection:

1. Create environment `Nova — Local Dev` → `baseUrl = http://localhost:<port>`
2. Create environment `Nova — Mock` → `baseUrl = https://<generated-id>.mock.pstmn.io`
3. Switch environments using the top-right environment picker

---

## Port reference

| Service | Local port |
|---|---|
| Nova.Shell.Api | 5100 |
| Nova.ToDo.Api | 5101 |

---

## Notes for the UX / frontend team

- The mock server returns **static** responses — it does not execute business logic or validate inputs. Use the `x-mock-response-name` header to simulate error states explicitly.
- The mock server is always available regardless of whether the .NET service is running.
- Auto-injected context fields (`tenant_id`, `company_id`, `branch_id`, `user_id`, `browser_locale`, `browser_timezone`, `ip_address`) must be present in every POST/PATCH request body. The mock server ignores them, but the real service requires them. Include them from the start so frontend code works against both.
- `account_code_client` and `supplier_code` have leading zeros that are significant — always treat as strings, never as integers.
