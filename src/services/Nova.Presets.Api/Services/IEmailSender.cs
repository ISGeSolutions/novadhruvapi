namespace Nova.Presets.Api.Services;

/// <summary>Abstraction for sending transactional email.</summary>
public interface IEmailSender
{
    Task SendAsync(
        string to,
        string subject,
        string plainTextBody,
        CancellationToken ct = default);
}
