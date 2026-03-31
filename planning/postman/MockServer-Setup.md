# Nova.Shell.Api — Postman Mock Server Setup

## Files in this folder

| File | Purpose |
|---|---|
| `Nova.Shell.Api.mock.postman_collection.json` | Import this to create the mock server |


## Step 1 — Import the collection

1. Open Postman
2. Click **Import** (top left)
3. Select `Nova.Shell.Api.mock.postman_collection.json`
4. Click **Import**

The collection **Nova.Shell.Api — Mock Server** will appear in your Collections sidebar.


## Step 2 — Create the Mock Server

1. In the Collections sidebar, hover over **Nova.Shell.Api — Mock Server**
2. Click the **...** (three dots) menu → **Mock collection**
3. Fill in the form:
   - **Mock server name**: `Nova.Shell.Api`
   - **Environment**: leave as No Environment (or create a new one — see Step 3)
   - **Make mock server private**: tick this if you do not want it publicly accessible
4. Click **Create Mock Server**
5. Postman generates a URL in the format:
   ```
   https://<unique-id>.mock.pstmn.io
   ```
   **Copy this URL** — you will need it in the next step.


## Where does baseUrl come from?

The API's listen URL is defined in two places in the project:

| Context | File | URL |
|---|---|---|
| Local development (`dotnet run`) | `src/services/Nova.Shell.Api/Properties/launchSettings.json` | `http://localhost:5100` |
| Production / Docker | `src/services/Nova.Shell.Api/appsettings.json` → `Kestrel:Endpoints:Http:Url` | `http://0.0.0.0:5100` |

When hitting the **real API** (not the mock), set `baseUrl` to `http://localhost:5100`.
When hitting the **Postman mock server**, set it to the `https://<id>.mock.pstmn.io` URL from Step 2.

Use a Postman Environment (one per target) to switch between them without editing the collection.


## Step 3 — Set the baseUrl variable

### Option A — Edit directly in the collection (simplest, mock only)

1. In the Collections sidebar, click **Nova.Shell.Api — Mock Server**
2. Go to the **Variables** tab
3. Set the **Current Value** of `baseUrl` to the mock server URL from Step 2
4. Click **Save**

### Option B — Use Postman Environments (recommended — switch between mock and real API)

Create two environments so you can toggle between the Postman mock and a locally running API without changing the collection.

**Environment 1 — Mock**
1. Click **Environments** (left sidebar) → **+**
2. Name it `Nova.Shell — Mock`
3. Add variable `baseUrl` → value: the `https://<id>.mock.pstmn.io` URL from Step 2
4. Click **Save**

**Environment 2 — Local**
1. Click **Environments** → **+**
2. Name it `Nova.Shell — Local`
3. Add variable `baseUrl` → value: `http://localhost:5100`  *(from `launchSettings.json`)*
4. Click **Save**

To switch targets, select the environment from the selector in the top-right corner of Postman.


## Step 4 — Send your first request

1. Open the collection → **Diagnostics** → **GET /hello-world**
2. Click **Send**
3. You should receive:

```json
{
  "message": "Hello, World!",
  "timestamp": "2026-03-13T10:00:00Z",
  "correlationId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
}
```

Postman matches the incoming request path and method to the saved examples in the collection and returns the matching response body and headers.


## How Postman Mock Matching Works

Postman matches requests to examples using this priority order:

| Priority | Match rule |
|---|---|
| 1 | Method + full URL path + `x-mock-response-name` header (exact name match) |
| 2 | Method + full URL path + `x-mock-response-code` header (status code match) |
| 3 | Method + full URL path (first example wins) |

Since each endpoint has both a success and a failure example, the success example is returned by default (it is listed first). To get a failure response, send the `x-mock-response-name` header.


## Triggering Error Responses

Each endpoint has a separate failure request pre-configured with the `x-mock-response-name` header already set. To test an error scenario:

1. Open the failure variant, e.g. **GET /test-db/mssql — Connection Failure**
2. Click **Send**
3. You will receive the 503 response

Alternatively, on any request you can manually add the header:

| Header | Value |
|---|---|
| `x-mock-response-name` | The exact example name, e.g. `503 Service Unavailable — Connection failed` |

Or to match by status code:

| Header | Value |
|---|---|
| `x-mock-response-code` | `503` |


## All Available Responses

| Request | Default response | Failure response (use x-mock-response-name) |
|---|---|---|
| `GET /hello-world` | 200 Hello World | — |
| `GET /test-db/mssql` | 200 Rows returned | `503 Service Unavailable — Connection failed` |
| `GET /test-db/postgres` | 200 Rows returned | `503 Service Unavailable — Connection failed` |
| `GET /health` | 200 All healthy | `503 Service Unavailable — Degraded` |
| `GET /health/mssql` | 200 MSSQL healthy | `503 Service Unavailable — MSSQL down` |
| `GET /health/postgres` | 200 Postgres healthy | `503 Service Unavailable — Postgres down` |


## Adding New Mock Responses Later

When you add new endpoints to Nova.Shell.Api:

1. Add a new request to this collection with the correct method and path
2. Click **Save as Example** after filling in the example response body, headers, and status code
3. The mock server picks up new examples automatically — no redeployment needed
