namespace Nova.CommonUX.Api.Configuration;

/// <summary>
/// OAuth social login provider credentials. Stored in <c>opsettings.json → SocialLogin</c>.
/// </summary>
public sealed class SocialLoginSettings
{
    public const string SectionName = "SocialLogin";

    public SocialProviderSettings Google    { get; set; } = new();
    public SocialProviderSettings Microsoft { get; set; } = new();
    public SocialProviderSettings Apple     { get; set; } = new();
}

public sealed class SocialProviderSettings
{
    public string ClientId     { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
