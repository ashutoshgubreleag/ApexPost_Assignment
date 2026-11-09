namespace ApexPost.Courier.Core.Api;

/// <summary>Settings of the API connection. The MAUI app fills them from appsettings and the current platform.</summary>
public sealed class ApiOptions
{
    /// <summary>Server root, for example <c>https://localhost:7153/</c> (Windows) or <c>https://10.0.2.2:7153/</c> (Android emulator).</summary>
    public Uri BaseAddress { get; set; } = new("https://localhost:7153/");

    /// <summary>Scan upload route; <c>{0}</c> is the tracking number. Matches the versioned route from Part C.</summary>
    public string ScansPathTemplate { get; set; } = "api/v2/shipments/{0}/scans";

    /// <summary>Development token route (see SECURITY.md): POST { courierId, role } returns { accessToken, expiresAtUtc }. Relative to <see cref="BaseAddress"/>.</summary>
    public string TokenPath { get; set; } = "dev/token";

    /// <summary>Scans sent per request (the server accepts at most 100).</summary>
    public int MaxScansPerRequest { get; set; } = 50;

    public ResilienceSettings Resilience { get; set; } = new();
}

/// <summary>Numbers behind the retry, circuit breaker and timeouts (explained in RESILIENCE.md).</summary>
public sealed class ResilienceSettings
{
    /// <summary>Retries after the first attempt.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>First retry delay; later ones double (exponential) and get random jitter.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Open the circuit when this share of calls in the sampling window failed.</summary>
    public double CircuitFailureRatio { get; set; } = 0.5;

    /// <summary>The circuit never opens before this many calls happened in the window (Polly minimum is 2).</summary>
    public int CircuitMinimumThroughput { get; set; } = 4;

    public TimeSpan CircuitSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the circuit stays open before one trial call is allowed.</summary>
    public TimeSpan CircuitBreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Limit for one attempt.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Limit for the whole call including every retry.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(40);
}
