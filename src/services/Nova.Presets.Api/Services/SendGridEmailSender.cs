using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nova.Presets.Api.Configuration;
using Nova.Shared.Security;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Nova.Presets.Api.Services;

/// <summary>
/// SendGrid-backed implementation of <see cref="IEmailSender"/>.
/// API key is CipherService-encrypted in <c>appsettings.json → Email.SendGrid.ApiKey</c>.
/// Sender address/name are in <c>opsettings.json → Email.SendGrid</c>.
/// </summary>
internal sealed class SendGridEmailSender : IEmailSender
{
    private readonly IOptionsMonitor<EmailSettings> _emailMonitor;
    private readonly ICipherService                 _cipher;
    private readonly ILogger<SendGridEmailSender>   _logger;

    public SendGridEmailSender(
        IOptionsMonitor<EmailSettings> emailMonitor,
        ICipherService                 cipher,
        ILogger<SendGridEmailSender>   logger)
    {
        _emailMonitor = emailMonitor;
        _cipher       = cipher;
        _logger       = logger;
    }

    public async Task SendAsync(string to, string subject, string plainTextBody, CancellationToken ct = default)
    {
        EmailSettings settings = _emailMonitor.CurrentValue;
        string        apiKey   = _cipher.Decrypt(settings.SendGrid.ApiKey);

        var client  = new SendGridClient(apiKey);
        var from    = new EmailAddress(settings.SendGrid.SenderAddress, settings.SendGrid.SenderDisplayName);
        var toAddr  = new EmailAddress(to);
        var message = MailHelper.CreateSingleEmail(from, toAddr, subject, plainTextBody, htmlContent: null);

        Response response = await client.SendEmailAsync(message, ct);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Body.ReadAsStringAsync(ct);
            _logger.LogError("SendGrid error {Status} sending to {To}: {Body}", response.StatusCode, to, body);
            throw new InvalidOperationException($"Email delivery failed (SendGrid status: {response.StatusCode}).");
        }

        _logger.LogDebug("Email sent via SendGrid to {To} — subject: {Subject}", to, subject);
    }
}
