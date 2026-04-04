using System.Text;
using Microsoft.Extensions.Options;
using Nova.Shared.Configuration;
using Nova.Shared.Messaging.Outbox;
using Nova.Shared.Security;
using RabbitMQ.Client;

namespace Nova.Shared.Web.Messaging;

/// <summary>
/// Publishes outbox messages to RabbitMQ via a persistent channel per publish call.
/// </summary>
/// <remarks>
/// The connection is created lazily on the first publish call, not at DI resolution time.
/// This means a missing or misconfigured RabbitMQ does not crash the service at startup —
/// only tenants with <c>BrokerType = RabbitMq</c> that have pending messages will surface
/// the connection error, and only when the relay attempts to publish.
///
/// Each publish creates and disposes a channel — channels are cheap, connections are not.
/// Messages are published as persistent (delivery mode 2), so they survive broker restart.
/// </remarks>
internal sealed class RabbitMqPublisher : IMessagePublisher, IDisposable
{
    private readonly IOptions<AppSettings> _appOptions;
    private readonly ICipherService        _cipher;
    private          IConnection?          _connection;
    private readonly object                _lock = new();

    public RabbitMqPublisher(
        IOptions<AppSettings> appOptions,
        ICipherService        cipher)
    {
        _appOptions = appOptions;
        _cipher     = cipher;
    }

    public Task PublishAsync(OutboxMessage message, CancellationToken ct = default)
    {
        IConnection conn = GetOrCreateConnection();

        using IModel channel = conn.CreateModel();

        IBasicProperties props = channel.CreateBasicProperties();
        props.ContentType  = message.ContentType;
        props.DeliveryMode = 2;   // persistent
        props.MessageId    = message.Id;

        if (message.CorrelationId is not null)
            props.CorrelationId = message.CorrelationId;

        byte[] body = Encoding.UTF8.GetBytes(message.Payload);

        channel.BasicPublish(
            exchange:        message.Exchange,
            routingKey:      message.RoutingKey,
            basicProperties: props,
            body:            body);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private IConnection GetOrCreateConnection()
    {
        if (_connection is { IsOpen: true })
            return _connection;

        lock (_lock)
        {
            if (_connection is { IsOpen: true })
                return _connection;

            RabbitMqSettings settings    = _appOptions.Value.RabbitMq;
            string           plainPassword = _cipher.Decrypt(settings.Password);

            var factory = new ConnectionFactory
            {
                HostName    = settings.Host,
                Port        = settings.Port,
                UserName    = settings.Username,
                Password    = plainPassword,
                VirtualHost = settings.VirtualHost,
            };

            _connection = factory.CreateConnection("nova-outbox-relay");
            return _connection;
        }
    }
}
