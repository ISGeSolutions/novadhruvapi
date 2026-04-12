namespace Nova.CommonUX.Api.Configuration;

/// <summary>
/// Email delivery settings. Stored in <c>opsettings.json → Email</c>.
/// Sensitive values (API keys) are in <c>appsettings.json</c> (CipherService-encrypted).
/// </summary>
public sealed class EmailSettings
{
    public const string SectionName = "Email";

    /// <summary>Active provider. <c>SendGrid</c> or <c>MicrosoftGraph</c>. Default: <c>SendGrid</c>.</summary>
    public string Provider { get; set; } = "SendGrid";

    public SendGridEmailSettings SendGrid { get; set; } = new();
}

public sealed class SendGridEmailSettings
{
    /// <summary>From address for all outbound email. Set in opsettings.json.</summary>
    public string SenderAddress { get; set; } = string.Empty;

    /// <summary>Display name for the from address. Set in opsettings.json.</summary>
    public string SenderDisplayName { get; set; } = string.Empty;

    /// <summary>SendGrid API key — CipherService-encrypted. Set in appsettings.json.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
