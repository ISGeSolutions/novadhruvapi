using Microsoft.Extensions.Logging;

namespace Nova.Presets.Api.Services;

/// <summary>
/// No-op email sender used when no SendGrid API key is configured.
/// Logs the email details instead of delivering them — for local development only.
/// </summary>
internal sealed class NoOpEmailSender : IEmailSender
{
    private readonly ILogger<NoOpEmailSender> _logger;

    public NoOpEmailSender(ILogger<NoOpEmailSender> logger) => _logger = logger;

    public Task SendAsync(string to, string subject, string plainTextBody, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "[NoOpEmailSender] Email not sent — no SendGrid API key configured. " +
            "To: {To} | Subject: {Subject} | Body: {Body}",
            to, subject, plainTextBody);

        return Task.CompletedTask;
    }
}
