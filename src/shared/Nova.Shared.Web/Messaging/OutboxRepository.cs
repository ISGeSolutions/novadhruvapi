using System.Data;
using Dapper;
using Nova.Shared.Data;
using Nova.Shared.Messaging.Outbox;
using Nova.Shared.Tenancy;
using NovaDbType = Nova.Shared.Data.DbType;

namespace Nova.Shared.Web.Messaging;

/// <summary>Data access for the <c>nova_outbox</c> table.</summary>
internal interface IOutboxRepository
{
    Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(
        TenantRecord tenant, string rawConnStr, int batchSize, CancellationToken ct);

    Task MarkProcessingAsync(
        TenantRecord tenant, string rawConnStr, IEnumerable<string> ids, CancellationToken ct);

    Task MarkSentAsync(
        TenantRecord tenant, string rawConnStr, string id, CancellationToken ct);

    Task RecordFailedAttemptAsync(
        TenantRecord tenant, string rawConnStr, string id, string error, CancellationToken ct);
}

/// <inheritdoc />
internal sealed class OutboxRepository : IOutboxRepository
{
    private readonly IDbConnectionFactory _factory;

    public OutboxRepository(IDbConnectionFactory factory) => _factory = factory;

    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(
        TenantRecord tenant, string rawConnStr, int batchSize, CancellationToken ct)
    {
        string sql = FetchPendingSql(tenant.DbType);

        using IDbConnection conn = _factory.OpenRaw(rawConnStr, tenant.DbType);
        IEnumerable<OutboxRow> rows = await conn.QueryAsync<OutboxRow>(sql, new { BatchSize = batchSize });

        return rows.Select(r => r.ToMessage(tenant.TenantId)).ToList();
    }

    public async Task MarkProcessingAsync(
        TenantRecord tenant, string rawConnStr, IEnumerable<string> ids, CancellationToken ct)
    {
        const string sql =
            "UPDATE nova_outbox SET status = 'processing' WHERE id IN @Ids AND status = 'pending'";

        using IDbConnection conn = _factory.OpenRaw(rawConnStr, tenant.DbType);
        await conn.ExecuteAsync(sql, new { Ids = ids.ToArray() });
    }

    public async Task MarkSentAsync(
        TenantRecord tenant, string rawConnStr, string id, CancellationToken ct)
    {
        string sql = MarkSentSql(tenant.DbType);
        using IDbConnection conn = _factory.OpenRaw(rawConnStr, tenant.DbType);
        await conn.ExecuteAsync(sql, new { Id = id });
    }

    public async Task RecordFailedAttemptAsync(
        TenantRecord tenant, string rawConnStr, string id, string error, CancellationToken ct)
    {
        // Increment retry count. If the new count reaches max_retries → mark failed permanently.
        // Otherwise reset to pending so the relay retries on the next cycle.
        const string sql = """
            UPDATE nova_outbox
            SET retry_count = retry_count + 1,
                last_error  = @Error,
                status      = CASE WHEN retry_count + 1 >= max_retries THEN 'failed' ELSE 'pending' END
            WHERE id = @Id
            """;

        using IDbConnection conn = _factory.OpenRaw(rawConnStr, tenant.DbType);
        await conn.ExecuteAsync(sql, new { Id = id, Error = error });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dialect-specific SQL
    // ─────────────────────────────────────────────────────────────────────────

    private static string FetchPendingSql(NovaDbType dbType) => dbType switch
    {
        NovaDbType.MsSql => """
            SELECT TOP (@BatchSize)
                CAST(id AS NVARCHAR(36)) AS Id,
                aggregate_id  AS AggregateId,
                event_type    AS EventType,
                payload       AS Payload,
                created_at    AS CreatedAt,
                processed_at  AS ProcessedAt,
                retry_count   AS RetryCount,
                last_error    AS LastError,
                exchange      AS Exchange,
                routing_key   AS RoutingKey,
                content_type  AS ContentType,
                max_retries   AS MaxRetries,
                status        AS Status,
                scheduled_on  AS ScheduledOn,
                correlation_id AS CorrelationId
            FROM nova_outbox
            WHERE status = 'pending'
              AND (scheduled_on IS NULL OR scheduled_on <= SYSUTCDATETIME())
            ORDER BY created_at ASC
            """,

        NovaDbType.Postgres => """
            SELECT
                id::text      AS Id,
                aggregate_id  AS AggregateId,
                event_type    AS EventType,
                payload       AS Payload,
                created_at    AS CreatedAt,
                processed_at  AS ProcessedAt,
                retry_count   AS RetryCount,
                last_error    AS LastError,
                exchange      AS Exchange,
                routing_key   AS RoutingKey,
                content_type  AS ContentType,
                max_retries   AS MaxRetries,
                status        AS Status,
                scheduled_on  AS ScheduledOn,
                correlation_id AS CorrelationId
            FROM nova_outbox
            WHERE status = 'pending'
              AND (scheduled_on IS NULL OR scheduled_on <= NOW() AT TIME ZONE 'UTC')
            ORDER BY created_at ASC
            LIMIT @BatchSize
            """,

        NovaDbType.MariaDb => """
            SELECT
                id            AS Id,
                aggregate_id  AS AggregateId,
                event_type    AS EventType,
                payload       AS Payload,
                created_at    AS CreatedAt,
                processed_at  AS ProcessedAt,
                retry_count   AS RetryCount,
                last_error    AS LastError,
                exchange      AS Exchange,
                routing_key   AS RoutingKey,
                content_type  AS ContentType,
                max_retries   AS MaxRetries,
                status        AS Status,
                scheduled_on  AS ScheduledOn,
                correlation_id AS CorrelationId
            FROM nova_outbox
            WHERE status = 'pending'
              AND (scheduled_on IS NULL OR scheduled_on <= UTC_TIMESTAMP(6))
            ORDER BY created_at ASC
            LIMIT @BatchSize
            """,

        _ => throw new NotSupportedException($"Unsupported DbType: {dbType}")
    };

    private static string MarkSentSql(NovaDbType dbType) => dbType switch
    {
        NovaDbType.MsSql    => "UPDATE nova_outbox SET status = 'sent', processed_at = SYSUTCDATETIME()            WHERE id = @Id",
        NovaDbType.Postgres => "UPDATE nova_outbox SET status = 'sent', processed_at = NOW() AT TIME ZONE 'UTC'   WHERE id = @Id",
        NovaDbType.MariaDb  => "UPDATE nova_outbox SET status = 'sent', processed_at = UTC_TIMESTAMP(6)           WHERE id = @Id",
        _                   => throw new NotSupportedException($"Unsupported DbType: {dbType}")
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Internal row DTO (Dapper target)
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class OutboxRow
    {
        public string    Id            { get; set; } = string.Empty;
        public string    AggregateId   { get; set; } = string.Empty;
        public string    EventType     { get; set; } = string.Empty;
        public string    Payload       { get; set; } = string.Empty;
        public DateTime  CreatedAt     { get; set; }
        public DateTime? ProcessedAt   { get; set; }
        public int       RetryCount    { get; set; }
        public string?   LastError     { get; set; }
        public string    Exchange      { get; set; } = string.Empty;
        public string    RoutingKey    { get; set; } = string.Empty;
        public string    ContentType   { get; set; } = string.Empty;
        public int       MaxRetries    { get; set; }
        public string    Status        { get; set; } = string.Empty;
        public DateTime? ScheduledOn   { get; set; }
        public string?   CorrelationId { get; set; }

        public OutboxMessage ToMessage(string tenantId) => new()
        {
            Id            = Id,
            TenantId      = tenantId,
            CreatedOn     = new DateTimeOffset(CreatedAt, TimeSpan.Zero),
            ScheduledOn   = ScheduledOn.HasValue  ? new DateTimeOffset(ScheduledOn.Value,  TimeSpan.Zero) : null,
            ProcessedOn   = ProcessedAt.HasValue  ? new DateTimeOffset(ProcessedAt.Value,  TimeSpan.Zero) : null,
            Exchange      = Exchange,
            RoutingKey    = RoutingKey,
            Payload       = Payload,
            ContentType   = ContentType,
            RetryCount    = RetryCount,
            MaxRetries    = MaxRetries,
            LastError     = LastError,
            Status        = Status,
            CorrelationId = CorrelationId,
        };
    }
}
