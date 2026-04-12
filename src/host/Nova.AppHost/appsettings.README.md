# AppHost configuration guide

## appsettings.json

Committed infrastructure toggles for local development. Change these to control which
containers Aspire starts when the AppHost launches.

### Infrastructure

| Key | Type | Default | Description |
|---|---|---|---|
| `UseRedis` | bool | `true` | Starts a Redis container (persistent). Required by any service that uses caching, distributed locking, or the Redis-backed outbox relay. When `false`, services must fall back to InMemory equivalents. |
| `UseRabbitMQ` | bool | `false` | Starts a RabbitMQ container (persistent) with the management plugin. Set to `true` when at least one tenant in any service's `appsettings.json` has `BrokerType: RabbitMq`. Management UI: http://localhost:15672 (guest/guest). |
| `UseSeq` | bool | `true` | Starts a Seq container (persistent) for unified structured log storage across all services. Seq UI: http://localhost:5341. Set to `false` only if you have an external log sink configured. |

---

## appsettings.local.json

Machine-local secrets — **gitignored, never commit**.

Create this file by copying the placeholder below into
`src/host/Nova.AppHost/appsettings.local.json`:

```json
{
  "ENCRYPTION_KEY": "<your-encryption-key-here>"
}
```

| Key | Description |
|---|---|
| `ENCRYPTION_KEY` | Symmetric key used by `Nova.Cipher` to encrypt/decrypt connection strings and other secrets in each service's `appsettings.json`. Must match the key that was used when those values were encrypted. |

The AppHost reads this value and passes it to every service via `.WithEnvironment("ENCRYPTION_KEY", ...)`.
