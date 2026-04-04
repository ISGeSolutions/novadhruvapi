using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Nova.Shared.Configuration;
using Nova.Shared.Security;
using RabbitMQ.Client;

namespace Nova.Shell.Api.HealthChecks;

/// <summary>Health check that verifies connectivity to the RabbitMQ broker.</summary>
public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IOptions<AppSettings> _appOptions;
    private readonly ICipherService        _cipher;

    public RabbitMqHealthCheck(IOptions<AppSettings> appOptions, ICipherService cipher)
    {
        _appOptions = appOptions;
        _cipher     = cipher;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken  cancellationToken = default)
    {
        try
        {
            RabbitMqSettings settings      = _appOptions.Value.RabbitMq;
            string           plainPassword = _cipher.Decrypt(settings.Password);

            var factory = new ConnectionFactory
            {
                HostName               = settings.Host,
                Port                   = settings.Port,
                UserName               = settings.Username,
                Password               = plainPassword,
                VirtualHost            = settings.VirtualHost,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
            };

            using IConnection conn = factory.CreateConnection("nova-health-check");
            return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ connection is healthy."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ connection failed.", ex));
        }
    }
}
