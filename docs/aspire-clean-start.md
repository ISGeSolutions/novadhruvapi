# Aspire Clean Start Guide

Use this when `aspire run` is stuck on **"Connecting to apphost..."** or services are misbehaving and a clean restart is needed.

---

## Why "Connecting to apphost..." gets stuck

The Aspire CLI shows "Connecting to apphost..." while it waits for the AppHost process to initialise and open its backchannel. It stays stuck when:

1. **Orphaned AppHost processes** from previous sessions are still running and have already claimed ports or locked container names.
2. **Containers are in `Exited` state** — a previous AppHost was killed mid-run, leaving containers stopped but not removed. The new AppHost tries to start them and conflicts with the old ones.
3. **Multiple `aspire run` instances** were started in quick succession (e.g. Ctrl+C then immediate retry) leaving child processes alive in the background.

The fix is always the same: kill all stale processes, remove exited containers, then start once cleanly.

---

## Step 1 — Diagnose

Run both of these to understand the current state:

```bash
# Show all containers (running AND stopped/exited)
docker ps -a

# Show all Aspire/AppHost processes
ps aux | grep -E "Nova.AppHost|aspire" | grep -v grep
```

**Healthy state** — one `aspire run`, one `dotnet run ... Nova.AppHost`, one `Nova.AppHost` binary, containers `Up`.

**Unhealthy state** — multiple AppHost processes at different timestamps, containers showing `Exited (137)` or `Exited (143)`.

---

## Step 2 — Kill all AppHost processes

```bash
pkill -f "Nova.AppHost"
pkill -f "aspire run"
```

Wait a second, then verify:

```bash
ps aux | grep -E "Nova.AppHost|aspire" | grep -v grep
```

If any processes remain (they may show state `?E` which means zombie/dead — those are harmless and clear automatically), kill them by PID:

```bash
kill -9 <PID> <PID> ...
```

`?E` (zombie) processes do not need to be killed — the OS reaps them on its own.

---

## Step 3 — Remove exited containers

```bash
docker ps -a
```

If you see containers with status `Exited`, remove them:

```bash
docker rm -f <container-name> <container-name>
```

For this project the container names are `redis-<hash>` and `seq-<hash>` (the hash suffix changes each time Aspire generates new names). Copy the exact names from the `docker ps -a` output.

**Shortcut — remove all exited containers at once:**

```bash
docker container prune -f
```

This only removes stopped containers, not running ones.

---

## Step 4 — Start cleanly

From the project root:

```bash
aspire run
```

Start it **once and wait**. Do not Ctrl+C and retry if it seems slow — check the log first (see below).

---

## How long should startup take?

| Scenario | Expected time |
|---|---|
| Images already downloaded, containers existed (persistent) | 5–15 seconds |
| Images already downloaded, containers removed | 15–30 seconds |
| Images not yet downloaded (first run after `docker rmi`) | 3–10 minutes (Seq is 1.1 GB) |

During image downloads, `docker ps -a` will be **empty** — this is normal. Docker does not create a container until after the image pull is complete.

---

## Checking progress during startup

**Check if images are downloading:**

```bash
docker images
```

Partially downloaded images appear with `<none>` tag. Completed pulls show the full tag.

**Watch Docker events in real time** (separate terminal):

```bash
docker events --filter type=image
docker events --filter type=container
```

**Check the Aspire CLI log:**

```bash
ls -t ~/.aspire/logs/ | head -3
cat ~/.aspire/logs/<latest-log-file>
```

The log shows:
- Build output (fast, ~10 seconds)
- A gap during image pulls / container startup (silent in CLI, visible in Docker events)
- `Now listening on: https://localhost:XXXXX` — this is when the dashboard is ready
- `Login to the dashboard at https://localhost:XXXXX/login?t=<token>` — use this URL

---

## Reading the Aspire log — key lines

```
[INFO] [Build] Build succeeded.           ← AppHost compiled OK
[WARN] Resource 'redis' has a persistent  ← See "Persistent container warning" below
       lifetime but...
[INFO] Now listening on: https://...      ← Dashboard is up — open this URL
[INFO] Login to the dashboard at https:// ← Token-authenticated dashboard URL
[INFO] GetDashboardUrlsAsync called       ← CLI backchannel connected — "Connecting..." resolved
```

---

## Persistent container warning

You may see:

```
Resource 'redis' has a persistent lifetime but the AppHost project does not have
user secrets configured. Generated parameter values may change on each restart,
causing persistent containers to be recreated.
```

**This is harmless for Redis and Seq** as configured in this project (neither uses a generated password). However, to suppress the warning permanently and ensure container identity is stable across restarts, run once:

```bash
cd src/host/Nova.AppHost
dotnet user-secrets init
```

This adds a `UserSecretsId` to `Nova.AppHost.csproj`. It only needs to be done once per machine.

---

## Container progress messages

The AppHost has a `ContainerProgressService` (`Program.cs`) that prints container state transitions to the console:

```
[22:31:05] ⬇  Downloading image for 'redis'...
[22:31:48] ▶  Starting container 'redis'...
[22:31:49] ✅  'redis' is ready.
```

These appear in the **AppHost process output**, which the Aspire CLI forwards to your terminal. If you don't see them, the AppHost may not have rebuilt yet — run `dotnet build src/host/Nova.AppHost/Nova.AppHost.csproj` before `aspire run`.

---

## Quick reference — full clean start sequence

```bash
# 1. Kill everything
pkill -f "Nova.AppHost" ; pkill -f "aspire run"

# 2. Remove exited containers
docker container prune -f

# 3. Verify clean
ps aux | grep -E "Nova.AppHost|aspire" | grep -v grep
docker ps -a

# 4. Start
aspire run
```

---

## When NOT to do a clean start

- If `aspire run` just launched and is still within the first 2 minutes — it may be downloading images. Check `docker events` before killing anything.
- If only one service is unhealthy in the Aspire dashboard — restart that service from the dashboard rather than killing the whole stack.
- If a container is `Up` but a service is erroring — the problem is in the service code or config, not the infrastructure.
