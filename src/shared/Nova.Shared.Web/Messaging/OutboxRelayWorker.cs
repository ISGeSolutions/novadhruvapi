using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nova.Shared.Configuration;
using Nova.Shared.Locking;
using Nova.Shared.Messaging;
using Nova.Shared.Messaging.Outbox;
using Nova.Shared.Security;
using Nova.Shared.Tenancy;

namespace Nova.Shared.Web.Messaging;

/// <summary>
/// Background service that relays pending <c>nova_outbox</c> messages to the appropriate
/// message broker (RabbitMQ or Redis) for each tenant.
/// </summary>
/// <remarks>
/// <para><b>Per-tenant distributed locking</b></para>
/// Before processing a tenant, the worker acquires a Redis lock keyed to
/// <c>nova:outbox-relay:{tenantId}</c>. If the lock cannot be acquired (another instance
/// already processing that tenant), the tenant is skipped for this cycle.
///
/// <para><b>Retry behaviour</b></para>
/// Each failed publish increments <c>retry_count</c>. Once <c>retry_count >= max_retries</c>
/// the message is permanently marked <c>failed</c> and requires manual intervention.
/// Messages remain in <c>pending</c> state between retry attempts.
///
/// <para><b>Hot-reloadable settings</b></para>
/// <see cref="OutboxRelaySettings.Enabled"/>, <see cref="OutboxRelaySettings.PollingIntervalSeconds"/>,
/// and <see cref="OutboxRelaySettings.BatchSize"/> are read from <c>IOptionsMonitor</c> on every
/// polling cycle — changes in <c>opsettings.json</c> take effect without a restart.
/// </remarks>
internal sealed class OutboxRelayWorker : BackgroundService
{
    private readonly TenantRegistry                  _tenantRegistry;
    private readonly ICipherService                  _cipher;
    private readonly IOutboxRepository               _repository;
    private readonly IDistributedLockService         _lockService;
    private readonly IOptionsMonitor<OpsSettings>    _opsMonitor;
    private readonly ILogger<OutboxRelayWorker>      _logger;
    private readonly IReadOnlyDictionary<BrokerType, IMessagePublisher> _publishers;

    public OutboxRelayWorker(
        TenantRegistry                                   tenantRegistry,
        ICipherService                                   cipher,
        IOutboxRepository                                repository,
        IDistributedLockService                          lockService,
        IOptionsMonitor<OpsSettings>                     opsMonitor,
        [FromKeyedServices("RabbitMq")] IMessagePublisher rabbitMqPublisher,
        [FromKeyedServices("Redis")]    IMessagePublisher redisPublisher,
        ILogger<OutboxRelayWorker>                       logger)
    {
        _tenantRegistry = tenantRegistry;
        _cipher         = cipher;
        _repository     = repository;
        _lockService    = lockService;
        _opsMonitor     = opsMonitor;
        _logger         = logger;

        _publishers = new Dictionary<BrokerType, IMessagePublisher>
        {
            [BrokerType.RabbitMq] = rabbitMqPublisher,
            [BrokerType.Redis]    = redisPublisher,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox relay worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            OutboxRelaySettings settings = _opsMonitor.CurrentValue.OutboxRelay;

            if (settings.Enabled)
            {
                foreach (TenantRecord tenant in _tenantRegistry.All)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        await ProcessTenantAsync(tenant, settings.BatchSize, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex,
                            "Outbox relay: unhandled error processing tenant {TenantId}", tenant.TenantId);
                    }
                }
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(settings.PollingIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) { /* shutting down */ }
        }

        _logger.LogInformation("Outbox relay worker stopped.");
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task ProcessTenantAsync(
        TenantRecord      tenant,
        int               batchSize,
        CancellationToken ct)
    {
        string lockKey = $"nova:outbox-relay:{tenant.TenantId}";

        await using IDistributedLock? @lock =
            await _lockService.TryAcquireAsync(lockKey, expiry: TimeSpan.FromSeconds(30), ct);

        if (@lock is null)
        {
            _logger.LogDebug(
                "Outbox relay: skipping tenant {TenantId} — lock held by another instance.", tenant.TenantId);
            return;
        }

        string rawConnStr = _cipher.Decrypt(tenant.ConnectionString);

        IReadOnlyList<OutboxMessage> messages =
            await _repository.FetchPendingAsync(tenant, rawConnStr, batchSize, ct);

        if (messages.Count == 0) return;

        _logger.LogDebug(
            "Outbox relay: {Count} pending message(s) for tenant {TenantId}",
            messages.Count, tenant.TenantId);

        await _repository.MarkProcessingAsync(
            tenant, rawConnStr, messages.Select(m => m.Id), ct);

        IMessagePublisher publisher = _publishers[tenant.BrokerType];

        foreach (OutboxMessage message in messages)
        {
            if (ct.IsCancellationRequested) break;
            await PublishOneAsync(tenant, rawConnStr, message, publisher, ct);
        }
    }

    private async Task PublishOneAsync(
        TenantRecord      tenant,
        string            rawConnStr,
        OutboxMessage     message,
        IMessagePublisher publisher,
        CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(message, ct);
            await _repository.MarkSentAsync(tenant, rawConnStr, message.Id, ct);

            _logger.LogDebug(
                "Outbox relay: sent {MessageId} [{Exchange}/{RoutingKey}] for tenant {TenantId}",
                message.Id, message.Exchange, message.RoutingKey, tenant.TenantId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Outbox relay: failed to publish {MessageId} for tenant {TenantId} — retry {Retry}/{Max}",
                message.Id, tenant.TenantId, message.RetryCount + 1, message.MaxRetries);

            await _repository.RecordFailedAttemptAsync(
                tenant, rawConnStr, message.Id, ex.Message, ct);
        }
    }
}
