# Distributed Locking — Developer Guide

This guide explains distributed locking from first principles for developers who are new to
Redis-based coordination. Read it before writing code that uses `IDistributedLockService`.

---

## 1. The problem — why `lock(obj)` is not enough

In C#, `lock(obj)` prevents two **threads** in the same process from running the same block
simultaneously. It works by setting a flag in process memory. The flag is only visible to
threads inside that one process.

In production, Nova runs as multiple containers (instances) — each has its own memory.
`lock(obj)` has no effect across containers.

```
┌─────────────────────────────────┐   ┌─────────────────────────────────┐
│  Container A (Instance 1)       │   │  Container B (Instance 2)       │
│                                 │   │                                 │
│  lock (obj) { ... }  ← flag    │   │  lock (obj) { ... }  ← flag    │
│              set in A's memory  │   │              set in B's memory  │
│                                 │   │                                 │
│  A's lock has zero effect on B  │   │  B's lock has zero effect on A  │
└─────────────────────────────────┘   └─────────────────────────────────┘
```

To coordinate between containers, the flag must live somewhere both can see —
**Redis** is that shared store.

---

## 2. The race condition without a distributed lock

This is the classic **check-then-act** race condition in a booking system:

```
Time →

Instance A:  reads seat 12A → available
                                          Instance B:  reads seat 12A → available
Instance A:  inserts booking for seat 12A ✓
                                          Instance B:  inserts booking for seat 12A ✓
                                                       ← TWO bookings for the same seat
```

Both instances read "available" before either writes. The check and the write are not
atomic — there is a window between them that another instance can exploit.

A distributed lock closes that window by making the check-then-write **exclusive**:
only the instance that holds the lock is allowed inside.

---

## 3. How Redis implements the lock — SET NX

Redis is single-threaded. It processes one command at a time. This makes it an ideal
coordination primitive.

### Acquiring the lock

```
SET seat:12A  "token-abc"  NX  PX  30000
              ↑             ↑   ↑   ↑
              value         |   |   30 000 ms TTL
                            |   expiry in milliseconds
                            only set if key does Not eXist
```

Redis checks and sets the key atomically in a single operation:
- Key does not exist → creates it, returns `OK` → **lock acquired**
- Key already exists → does nothing, returns `nil` → **lock not acquired** (held by someone else)

Because Redis is single-threaded, two simultaneous `SET NX` commands for the same key are
processed one after the other. The first one wins. The second one sees the key already
exists and returns `nil`. There is no race condition inside Redis.

### What the value ("token") is

The value stored is a **UUID generated fresh for each acquisition**:

```csharp
string token = Guid.NewGuid().ToString("N");  // e.g. "a3f7b2e1..."
```

This token is critical at release time — explained in section 5.

### Releasing the lock

To release, delete the key from Redis. But not with a simple `DEL` — explained next.

---

## 4. The TTL — why every lock must expire

The TTL (time-to-live) is not just an optimisation. It is the **safety net** for process
failure.

**Scenario: process crash while holding a lock**

```
Instance A acquires lock for seat:12A   (TTL: 30 s)
Instance A starts inserting the booking
Instance A crashes  ←───────────────────  process killed, DisposeAsync never runs
                                          lock is still in Redis
Without TTL:  seat:12A locked forever — no one can book it ever again
With TTL:     Redis removes the key after 30 s — lock released automatically
```

**Choosing the TTL** — it must be longer than the expected operation, with a buffer:

| Operation | Expected time | TTL to use |
|---|---|---|
| DB read + write | 1–5 s | 30 s |
| Payment gateway call | 5–20 s | 60 s |
| Background job run | varies | duration + 20 % |
| Cache rebuild | < 1 s | 10 s |

**What if the operation takes longer than the TTL?**
The lock expires, another instance can acquire it, and both instances are in the critical
section simultaneously. This is the reason you must also use a DB unique constraint as a
last line of defence — see section 8.

---

## 5. The unique token and why it prevents a silent bug

### The bug without a unique token

Imagine every lock acquisition stores the same static value, say `"locked"`:

```
Time →

Instance A acquires lock:  SET seat:12A "locked" NX PX 5000
Instance A starts work (takes 6 s — longer than TTL)
  → TTL expires at 5 s — Redis deletes the key automatically

Instance B acquires lock:  SET seat:12A "locked" NX PX 5000  → OK
  → Instance B is now inside the critical section

Instance A finishes work and calls DEL seat:12A
  → Instance A just deleted INSTANCE B'S lock
  → seat:12A is now unlocked while Instance B is still inside the critical section
  → Instance C can acquire immediately
  → Instances B and C are both in the critical section simultaneously
```

### The fix: unique token per acquisition

Each acquisition generates a fresh UUID:

```
Instance A acquires:  SET seat:12A "uuid-A" NX PX 5000
Instance A's TTL expires → Redis deletes the key
Instance B acquires:  SET seat:12A "uuid-B" NX PX 5000  → OK
Instance A finishes:  tries to release — checks if key == "uuid-A" → it is "uuid-B" → skip
```

Instance A's release attempt is a no-op because the stored value no longer matches its token.
Instance B's lock is safe.

---

## 6. The Lua script — why release cannot be two commands

To release safely, we need to:
1. Read the current value — check it is still our token
2. Delete the key — only if it matched

These must happen as a single atomic unit. As two separate Redis commands, they have a gap:

```
Instance A:  GET seat:12A  → "uuid-A"   (still ours)
                              ↑
                              TTL expires HERE
                              Instance B: SET seat:12A "uuid-B" NX → OK
                                          Instance B is now inside the critical section

Instance A:  DEL seat:12A               ← deletes Instance B's lock ← WRONG
```

### The Lua solution

Redis executes Lua scripts atomically — no other command from any client runs while the
script is executing:

```lua
if redis.call("GET", KEYS[1]) == ARGV[1] then
    return redis.call("DEL", KEYS[1])
else
    return 0
end
```

```
KEYS[1]  = the lock key        e.g. "seat:12A"
ARGV[1]  = our unique token    e.g. "uuid-A"
```

**What the script does, step by step:**
1. `GET KEYS[1]` — reads the current value of the key
2. Compares it to `ARGV[1]` (our token)
3. If they match → `DEL KEYS[1]` — delete the key (release the lock)
4. If they do not match → return `0` — do nothing (key is gone or belongs to another instance)

Because the entire script is atomic, there is no window between the GET and the DEL.

**What `LuaScript.Prepare` does (implementation detail):**
The script is compiled once and cached by its SHA-1 hash. On subsequent calls, only the
64-character hash is sent to Redis instead of the full script text. This is faster and
reduces network traffic.

---

## 7. The `await using` pattern — how C# releases the lock automatically

`IDistributedLock` implements `IAsyncDisposable`. The `await using` statement calls
`DisposeAsync()` automatically when the block exits, whether normally or via exception.

```csharp
await using IDistributedLock? lk = await _lockService.TryAcquireAsync(key, expiry, ct);

if (lk is null)
    return Results.Conflict(...);

// ── enter critical section ──────────────────────────────────
await _repo.CreateBookingAsync(request, ct);   // may throw
// ── exit critical section ───────────────────────────────────

// DisposeAsync() is called here by the compiler:
//   • whether CreateBookingAsync succeeded
//   • whether it threw an exception
//   • whether you returned early anywhere inside the block
```

**What happens if you forget `await using` and use `var` instead:**

```csharp
// ← WRONG — lock is never released until GC finaliser runs (non-deterministic)
var lk = await _lockService.TryAcquireAsync(key, expiry, ct);
await _repo.CreateBookingAsync(request, ct);
// lk.DisposeAsync() is never called — lock held until TTL expiry
```

**What happens if an exception is thrown inside the block:**

```csharp
await using IDistributedLock? lk = await _lockService.TryAcquireAsync(key, expiry, ct);
if (lk is null) return Results.Conflict(...);

await _repo.CreateBookingAsync(request, ct);   // ← throws DbException
// DisposeAsync() IS still called — lock released correctly
// The exception propagates and is handled by UseNovaProblemDetails
```

The lock is always released. You do not need a `try/finally` block.

---

## 8. Lock + DB constraint — defence in depth

A distributed lock prevents the race condition in the common case. A DB unique constraint
is the final safety net for the uncommon case (lock TTL expired mid-operation).

**Scenario: TTL expiry during operation**

```
Instance A acquires lock (TTL: 30 s)
Instance A reads — seat available
Instance A begins slow DB write (replication lag, large transaction)
   → 30 s passes — lock TTL expires — Redis deletes the key

Instance B acquires lock for the same seat
Instance B reads — seat still appears available (A has not committed yet)
Instance B inserts booking

Instance A's transaction commits — seat:12A booked by A
Instance B's INSERT runs:
   → WITH unique constraint:    INSERT fails with constraint violation — caught as error
   → WITHOUT unique constraint: two bookings inserted silently
```

**Both must be in place:**

```csharp
// 1. Lock prevents the race in the normal case
await using IDistributedLock? lk = await _lockService.TryAcquireAsync(lockKey, expiry, ct);
if (lk is null)
    return Results.Conflict("Already being processed.");

// 2. Business logic check (application layer)
bool exists = await _repo.BookingExistsAsync(request.BookingRef, ct);
if (exists)
    return Results.Conflict("Booking already exists.");

// 3. DB unique constraint (database layer — enforced by schema)
await _repo.CreateBookingAsync(request, ct);
// If constraint fires here: repo catches DbException → returns 409
```

```sql
-- Schema: the constraint that catches edge cases the lock misses
ALTER TABLE booking ADD CONSTRAINT uq_booking_ref UNIQUE (tenant_id, booking_ref);
```

---

## 9. What `null` means — Redis unavailable vs lock held

`TryAcquireAsync` returns `null` in two situations:
1. **Lock already held** — another instance acquired it first
2. **Redis is unavailable** — network partition, Redis restart, etc.

Both are treated the same way: return `null`, do not proceed.

**Why not proceed when Redis is unavailable?**
Because you cannot verify whether another instance holds the lock. Proceeding without
confirmation could cause the exact race condition the lock is meant to prevent.

**What to return to the client when `null`:**

```csharp
if (lk is null)
    return Results.Conflict(new ProblemDetails
    {
        Status = 409,
        Title  = "Request already in progress",
        Detail = "This booking reference is already being processed. " +
                 "Wait a moment and try again."
    });
```

`409 Conflict` is semantically correct — the resource state prevents the operation.
Do not use `503 Service Unavailable` — the client should retry, not give up.

---

## 10. Common mistakes

### Mistake 1 — Checking `null` after the critical section

```csharp
// WRONG — the critical section ran before the null check
await using IDistributedLock? lk = await _lockService.TryAcquireAsync(key, expiry, ct);
await _repo.CreateBookingAsync(request, ct);   // ← runs even if lk is null
if (lk is null) return Results.Conflict(...);  // ← too late
```

```csharp
// CORRECT — null check is the gate
await using IDistributedLock? lk = await _lockService.TryAcquireAsync(key, expiry, ct);
if (lk is null) return Results.Conflict(...);
await _repo.CreateBookingAsync(request, ct);
```

### Mistake 2 — Using a static/shared key for all operations

```csharp
// WRONG — every booking operation across all tenants blocks each other
await using IDistributedLock? lk = await _lockService.TryAcquireAsync(
    "booking-lock", expiry, ct);
```

```csharp
// CORRECT — key includes tenant and resource identifier
await using IDistributedLock? lk = await _lockService.TryAcquireAsync(
    $"tenant:{tenantId}:booking:create:{request.BookingRef}", expiry, ct);
```

### Mistake 3 — TTL shorter than the operation

```csharp
// WRONG — DB write takes 5 s, TTL is 3 s → lock expires before write completes
await using IDistributedLock? lk = await _lockService.TryAcquireAsync(
    key, TimeSpan.FromSeconds(3), ct);
```

```csharp
// CORRECT — generous TTL: expected time × 2 at minimum
await using IDistributedLock? lk = await _lockService.TryAcquireAsync(
    key, TimeSpan.FromSeconds(30), ct);
```

### Mistake 4 — Nested locks in different orders (deadlock)

```csharp
// WRONG — Instance A holds lock-1 waiting for lock-2
//          Instance B holds lock-2 waiting for lock-1 → deadlock
// Instance A:
await using var lk1 = await _lockService.TryAcquireAsync("lock-1", expiry, ct);
await using var lk2 = await _lockService.TryAcquireAsync("lock-2", expiry, ct);

// Instance B (same time, reverse order):
await using var lk2 = await _lockService.TryAcquireAsync("lock-2", expiry, ct);
await using var lk1 = await _lockService.TryAcquireAsync("lock-1", expiry, ct);
```

The TTL prevents permanent deadlock (both locks will eventually expire) but the window
of deadlock equals the TTL duration. Avoid nested locks wherever possible. If they are
unavoidable, always acquire in the same order across all code paths.

### Mistake 5 — Holding a lock across a user-facing wait

```csharp
// WRONG — user takes 30 s to fill a form; lock held the entire time
await using var lk = await _lockService.TryAcquireAsync(key, TimeSpan.FromMinutes(5), ct);
// ... show confirmation page, wait for user input ...
await _repo.CreateBookingAsync(confirmed, ct);
```

Locks are for server-side critical sections only. Acquire immediately before the write,
not at the start of a user flow.

---

## 11. How to inspect locks in Redis during debugging

**View all locks held right now:**
```bash
redis-cli KEYS "tenant:*"      # all tenant-scoped lock keys
redis-cli KEYS "nova:job:*"    # all job lock keys
```

**Inspect a specific lock:**
```bash
redis-cli GET "tenant:BTDK:booking:create:BK-001"
# returns the UUID token, or (nil) if not locked
```

**Check its remaining TTL (milliseconds):**
```bash
redis-cli PTTL "tenant:BTDK:booking:create:BK-001"
# > 0        = key exists, N ms remaining
# -1         = key exists but has no TTL (misconfigured — should never happen)
# -2         = key does not exist (not locked)
```

**Manually release a stuck lock (emergency only):**
```bash
redis-cli DEL "tenant:BTDK:booking:create:BK-001"
```
Use this in development or as a last resort in production if the TTL is unreasonably long
and you need to unblock a resource immediately.

**Using RedisInsight (GUI):**
RedisInsight is a free Redis GUI. Connect to `localhost:6379` and browse keys, view values,
and inspect TTLs visually. More convenient than `redis-cli` for exploratory debugging.

---

## 12. What to look for in the logs

`RedisDistributedLockService` and `RedisDistributedLock` emit structured log events.

| Event | Level | Message |
|---|---|---|
| Lock acquired | `Debug` | `Distributed lock acquired: {LockResource} (TTL: {ExpirySeconds}s)` |
| Lock not acquired (contention) | `Debug` | `Distributed lock not acquired — already held: {LockResource}` |
| Lock released | `Debug` | `Distributed lock released: {LockResource}` |
| Redis unavailable (acquire) | `Warning` | `Redis unavailable — could not acquire distributed lock for {LockResource}` |
| Redis unavailable (release) | `Warning` | `Redis unavailable — lock release skipped for {LockResource}. TTL will expire it automatically.` |

> Debug logs are suppressed at the default `Information` level. To see acquire/release
> events, temporarily set `Logging.DefaultLevel: "Debug"` in `opsettings.json` — it
> hot-reloads without a restart.

**What a healthy lock cycle looks like in logs:**

```
[DBG] Distributed lock acquired: tenant:BTDK:booking:create:BK-001 (TTL: 30s)
[DBG] Distributed lock released: tenant:BTDK:booking:create:BK-001
```

**What contention looks like:**

```
[DBG] Distributed lock acquired: tenant:BTDK:booking:create:BK-001 (TTL: 30s)
[DBG] Distributed lock not acquired — already held: tenant:BTDK:booking:create:BK-001
[DBG] Distributed lock not acquired — already held: tenant:BTDK:booking:create:BK-001
[DBG] Distributed lock released: tenant:BTDK:booking:create:BK-001
```

**What Redis unavailability looks like:**

```
[WRN] Redis unavailable — could not acquire distributed lock for tenant:BTDK:booking:create:BK-001
```

If you see this warning repeatedly, check `GET /health/redis` and verify the Redis
container is running.

---

## 13. Distributed locking vs other concurrency strategies

| Strategy | Where it lives | Best for | Limitation |
|---|---|---|---|
| `lock(obj)` | Process memory | Single-instance thread safety | No effect across containers |
| DB transaction + isolation level | Database | Consistent reads within a transaction | Does not prevent two transactions from both reading before either writes |
| DB unique constraint | Database | Last-line-of-defence for duplicate writes | Does not prevent the race — only catches it after the fact |
| DB `SELECT FOR UPDATE` | Database | Row-level locking inside a transaction | Holds a DB connection for the lock duration; does not work across DB calls |
| Distributed lock (Redis) | Redis | Cross-instance exclusive critical sections | Requires Redis; lock TTL must be carefully chosen |

**The recommended combination for booking/payment operations in Nova:**

1. **Distributed lock** — prevents the race condition (acquires before the check)
2. **Application check** — business logic guard (`BookingExistsAsync`)
3. **DB unique constraint** — catches any edge case the lock missed

All three layers together make the operation correct under all failure modes.

---

## 14. Quick reference

```csharp
// Standard pattern — copy this for any new endpoint that needs a lock

string lockKey = $"tenant:{tenantId}:{entity}:{identifier}";

await using IDistributedLock? lk = await _lockService.TryAcquireAsync(
    resource: lockKey,
    expiry:   TimeSpan.FromSeconds(30),   // longer than your operation + buffer
    ct:       ct);

if (lk is null)
    return Results.Conflict("Already being processed. Try again shortly.");

// ── critical section ─────────────────────────────────────────────────
// Check, then write.
// Only one instance across all containers executes this block at a time.
// ── end critical section ─────────────────────────────────────────────

// lock released here by await using — even on exception
```

**Checklist before merging code that uses `IDistributedLockService`:**

- [ ] `await using` (not `var`) used for the lock handle
- [ ] `null` check immediately after `TryAcquireAsync`
- [ ] Lock key includes `tenantId` and the specific resource identifier (not a generic key)
- [ ] TTL is at least 2× the expected operation duration
- [ ] A corresponding DB unique constraint exists on the table being written to
- [ ] No nested lock acquisitions (or if unavoidable, always in the same order)
- [ ] No `Task.Delay` or user-facing waits inside the critical section
