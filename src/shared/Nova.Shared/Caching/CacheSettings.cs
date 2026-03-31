namespace Nova.Shared.Caching;

/// <summary>Operational caching settings (part of <c>opsettings.json</c>).</summary>
public sealed class CacheSettings
{
    /// <summary>Master switch — when false, all caching is bypassed.</summary>
    public bool GloballyEnabled { get; set; } = true;

    /// <summary>Emergency disable switch overrides all profiles.</summary>
    public bool EmergencyDisable { get; set; }

    /// <summary>When true, cache lookups are performed but results are not returned (dry run).</summary>
    public bool DryRunMode { get; set; }

    /// <summary>Named cache profiles keyed by profile name.</summary>
    public Dictionary<string, CacheProfile> Profiles { get; set; } = [];

    /// <summary>Endpoint paths excluded from caching.</summary>
    public List<string> EndpointExclusions { get; set; } = [];
}
