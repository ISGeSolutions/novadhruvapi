# Running and Testing the API

## Prerequisites

`ENCRYPTION_KEY` must be set in your shell before starting the API. The app throws `InvalidOperationException` at boot if it is missing.

```bash
export ENCRYPTION_KEY=your-dev-key
```

The key must match the key used to encrypt the connection strings and JWT secret in `appsettings.json`.

---

## Running from the Terminal

All commands run from the repo root or the project directory.

### Standard web mode

```bash
cd src/services/Nova.Shell.Api
dotnet run
```

Listens on `http://localhost:5100`.

### Specify a launch profile explicitly

```bash
dotnet run --launch-profile http      # HTTP only — http://localhost:5100
dotnet run --launch-profile https     # HTTPS + HTTP — https://localhost:5101 + http://localhost:5100
dotnet run --launch-profile console   # Console mode (see below)
```

Profiles are defined in `src/services/Nova.Shell.Api/Properties/launchSettings.json`.

### Console mode

Runs the full web host but prints verbose startup output to stdout: config loaded, DB pings, tenant count, OTel endpoint. Useful for diagnosing startup issues.

```bash
dotnet run --launch-profile console
# or equivalently from any directory:
dotnet run --project src/services/Nova.Shell.Api -- --console
# or via environment variable:
RUN_AS_CONSOLE=true dotnet run --project src/services/Nova.Shell.Api
```

### Watch mode

Auto-restarts the API on file save. Use during active development.

```bash
cd src/services/Nova.Shell.Api
dotnet watch run
```

### From the repo root

```bash
dotnet run --project src/services/Nova.Shell.Api
dotnet run --project src/services/Nova.Shell.Api --launch-profile console
```

---

## Running from VS Code

Three debug configurations are provided in `.vscode/launch.json`:

| Configuration | Description |
|---|---|
| **Nova.Shell.Api — HTTP** | Normal web mode. Press F5 to start with the debugger attached. |
| **Nova.Shell.Api — Console mode** | Passes `--console` — startup pings appear in the Debug Console. |
| **Nova.Shell.Api — Attach to process** | Attach the debugger to an already-running `dotnet run` process. |

`ENCRYPTION_KEY` is inherited from your shell environment. Set it before launching VS Code, or add it to your shell profile (`~/.zshrc` / `~/.bashrc`).

### Setting breakpoints

Set breakpoints in any `.cs` file and use **F5** (Nova.Shell.Api — HTTP) to hit them. The debugger attaches automatically.

---

## Testing from Postman

### One-time setup

1. Import `planning/postman/Nova.Shell.Api.mock.postman_collection.json`
2. Create a **Local** environment:
   - Variable: `baseUrl` → `http://localhost:5100`
3. Create a **Mock** environment:
   - Variable: `baseUrl` → your Postman mock server URL (see `planning/postman/MockServer-Setup.md`)
4. Select the **Local** environment when hitting the running API

Switch between environments using the selector in the top-right corner of Postman.

### Endpoints

| Request | Expected (DB available) | Expected (DB down) |
|---|---|---|
| `GET {{baseUrl}}/hello-world` | 200 `{ message, timestamp, correlationId }` | — |
| `GET {{baseUrl}}/test-db/mssql` | 200 JSON array of `{ code, value }` | 503 `{ error, db: "mssql" }` |
| `GET {{baseUrl}}/test-db/postgres` | 200 JSON array of `{ code, value }` | 503 `{ error, db: "postgres" }` |
| `GET {{baseUrl}}/test-db/mysql` | 200 JSON array of `{ code, value }` | 503 `{ error, db: "mysql" }` |
| `GET {{baseUrl}}/health` | 200 all healthy | 503 degraded |
| `GET {{baseUrl}}/health/mssql` | 200 `{ status: "Healthy" }` | 503 `{ status: "Unhealthy" }` |
| `GET {{baseUrl}}/health/postgres` | 200 `{ status: "Healthy" }` | 503 `{ status: "Unhealthy" }` |
| `GET {{baseUrl}}/health/mysql` | 200 `{ status: "Healthy" }` | 503 `{ status: "Unhealthy" }` |

### Correlation ID

Every response includes `X-Correlation-ID` in the response headers. To trace a specific request, send your own value:

```
X-Correlation-ID: my-test-id-123
```

The same value echoes back in the response header and in the `/hello-world` response body.

### Testing mock error responses

To force an error response from the Postman mock server (not the real API), add:

| Header | Value |
|---|---|
| `x-mock-response-name` | e.g. `503 Service Unavailable — Connection failed` |
| `x-mock-response-code` | e.g. `503` |

See `planning/postman/MockServer-Setup.md` for the full list of available mock responses.

---

## Quick Reference

```bash
# Run (standard)
export ENCRYPTION_KEY=your-dev-key
dotnet run --project src/services/Nova.Shell.Api

# Run (console mode — verbose startup)
dotnet run --project src/services/Nova.Shell.Api -- --console

# Watch (auto-restart on save)
cd src/services/Nova.Shell.Api && dotnet watch run

# Build only
dotnet build src/services/Nova.Shell.Api

# Restore packages
dotnet restore
```
