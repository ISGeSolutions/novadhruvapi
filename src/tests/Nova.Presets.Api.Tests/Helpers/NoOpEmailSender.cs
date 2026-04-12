using Nova.Presets.Api.Services;

namespace Nova.Presets.Api.Tests.Helpers;

/// <summary>
/// Test-only <see cref="IEmailSender"/> that discards all outbound email.
/// Registered in <see cref="TestHost"/> so tests do not require a real SendGrid API key
/// and no email is delivered during test runs. Tests that assert email content must
/// use a different strategy (e.g. capture-and-inspect via a fake sender).
/// </summary>
internal sealed class NoOpEmailSender : IEmailSender
{
    public Task SendAsync(
        string            to,
        string            subject,
        string            plainTextBody,
        CancellationToken ct = default)
        => Task.CompletedTask;
}
