namespace ApexPost.Utilities.Options;

/// <summary>Section <c>MaintenanceMode</c>. Read through IOptionsMonitor, so it can be flipped without a restart.</summary>
public sealed class MaintenanceModeOptions
{
    public const string SectionName = "MaintenanceMode";

    public bool Enabled { get; set; }
    public int RetryAfterSeconds { get; set; } = 600;
    public string Message { get; set; } = "ApexPost Counter is undergoing scheduled maintenance. Please try again shortly.";

    /// <summary>Paths that stay available during maintenance (health probes).</summary>
    public string[] AlwaysAvailablePaths { get; set; } = ["/health"];
}

/// <summary>Section <c>IpRestriction</c>.</summary>
public sealed class IpRestrictionOptions
{
    public const string SectionName = "IpRestriction";

    public bool Enabled { get; set; } = true;

    /// <summary>Single addresses ("10.0.0.5") or CIDR ranges ("10.20.0.0/16").</summary>
    public string[] AllowList { get; set; } = ["127.0.0.1", "::1"];

    public string[] ProtectedPathPrefixes { get; set; } = ["/admin", "/integration"];
}

/// <summary>
/// Section <c>Integration</c>. Partner keys come from user-secrets or environment variables
/// (<c>Integration__Partners__swiftcourier</c>), never from appsettings.json in source control.
/// </summary>
public sealed class IntegrationOptions
{
    public const string SectionName = "Integration";

    public const string ApiKeyHeaderName = "X-Api-Key";

    /// <summary>Partner id → API key.</summary>
    public Dictionary<string, string> Partners { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Section <c>Performance</c>.</summary>
public sealed class PerformanceOptions
{
    public const string SectionName = "Performance";

    public int SlowActionThresholdMs { get; set; } = 500;
}

/// <summary>
/// Registered counter terminals (section <c>Sync</c>). Each device has its own key and is bound to
/// one branch. Keys come from user-secrets / environment variables
/// (<c>Sync__Devices__TERM-BR001-01__Key</c>), never from appsettings.json in source control.
/// </summary>
public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    public Dictionary<string, DeviceRegistration> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Most sales accepted in one push (the terminal sends batches).</summary>
    public int MaxSalesPerPush { get; set; } = 50;

    /// <summary>Push/pull requests allowed per device per minute.</summary>
    public int RequestsPerMinute { get; set; } = 30;
}

public sealed class DeviceRegistration
{
    public string Key { get; set; } = string.Empty;

    public string BranchCode { get; set; } = string.Empty;
}
