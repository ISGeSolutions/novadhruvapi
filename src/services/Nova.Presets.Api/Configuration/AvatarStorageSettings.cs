namespace Nova.Presets.Api.Configuration;

/// <summary>
/// Avatar file storage settings. Stored in <c>appsettings.json → AvatarStorage</c>.
/// </summary>
public sealed class AvatarStorageSettings
{
    public const string SectionName = "AvatarStorage";

    /// <summary>
    /// Absolute path to the directory where avatar files are written.
    /// Subdirectories per tenant are created automatically.
    /// Example: <c>C:\nova\avatars</c> or <c>/var/nova/avatars</c>.
    /// </summary>
    public string LocalDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Public base URL used to construct the avatar_url stored in the database.
    /// Example: <c>http://localhost:5103/avatars</c> or a CDN origin.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
}
