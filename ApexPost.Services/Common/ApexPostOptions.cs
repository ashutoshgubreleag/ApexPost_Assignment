namespace ApexPost.Services.Common;

/// <summary>Configurable business thresholds (section <c>BusinessRules</c>).</summary>
public sealed class BusinessRulesOptions
{
    public const string SectionName = "BusinessRules";

    /// <summary>Money orders strictly above this amount need a passed identity check in the same transaction.</summary>
    public decimal MoneyOrderIdentityThreshold { get; set; } = 1000m;

    /// <summary>Money orders at or above this amount trigger a high-value notification.</summary>
    public decimal HighValueMoneyOrderThreshold { get; set; } = 2500m;

    /// <summary>Largest money order a counter may issue.</summary>
    public decimal MaximumMoneyOrderAmount { get; set; } = 10000m;

    public decimal MoneyOrderFeeRate { get; set; } = 0.015m;
    public decimal MinimumMoneyOrderFee { get; set; } = 5.00m;
    public decimal IdentityVerificationFee { get; set; } = 2.50m;

    /// <summary>Stock at or below max(item reorder threshold, this value) counts as low.</summary>
    public int LowStockThreshold { get; set; } = 5;

    /// <summary>Attempts (including the first) for a use case that hits an optimistic concurrency conflict.</summary>
    public int ConcurrencyRetryAttempts { get; set; } = 3;

    /// <summary>Offline price variance (fraction) above which a synced sale is flagged for review (CONFLICTS.md C1).</summary>
    public decimal OfflinePriceVarianceTolerance { get; set; } = 0.05m;
}

/// <summary>Simulated courier partner settings (section <c>Courier</c>).</summary>
public sealed class CourierOptions
{
    public const string SectionName = "Courier";

    /// <summary>Concurrent calls allowed to the courier rate API (enforced with SemaphoreSlim).</summary>
    public int MaxConcurrentRateLookups { get; set; } = 4;

    public int RateLookupLatencyMs { get; set; } = 40;

    public int TrackingLatencyMs { get; set; } = 120;

    /// <summary>Tracking lookups slower than this are abandoned and reported as unavailable.</summary>
    public int TrackingTimeoutMs { get; set; } = 1500;

    /// <summary>Tracking numbers ending with this suffix simulate a hung courier API.</summary>
    public string SlowTrackingSuffix { get; set; } = "999";
}
