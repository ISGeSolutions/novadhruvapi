namespace Nova.CommonUX.Api.Services;

/// <summary>Abstraction for sending transactional email.</summary>
public interface IEmailSender
{
    /// <summary>Sends a plain-text email to a single recipient.</summary>
    Task SendAsync(
        string to,
        string subject,
        string plainTextBody,
        CancellationToken ct = default);
}
