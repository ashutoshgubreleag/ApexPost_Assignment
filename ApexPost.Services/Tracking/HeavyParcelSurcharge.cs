using Microsoft.Extensions.Options;

namespace ApexPost.Services.Tracking;

/// <summary>Decides the heavy-parcel surcharge for a tracked shipment (Week 5 TDD rule, see TDD-LOG.md).</summary>
public interface IShipmentSurchargePolicy
{
    /// <summary>The surcharge for a shipment of <paramref name="weightKg"/> booked at <paramref name="bookedAtUtc"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The weight is outside the service range (<see cref="TrackingLimits.MinWeightKg"/>–<see cref="TrackingLimits.MaxWeightKg"/>).</exception>
    decimal SurchargeFor(decimal weightKg, DateTimeOffset bookedAtUtc);

    /// <summary>The surcharge for a shipment of <paramref name="weightKg"/> booked now (the injected clock's "now").</summary>
    decimal QuoteNow(decimal weightKg);
}

/// <summary>One surcharge band: shipments heavier than <see cref="OverKg"/> pay <see cref="Amount"/> (the heaviest matching band wins).</summary>
public sealed record SurchargeTier(decimal OverKg, decimal Amount);

/// <summary>Settings of the heavy-parcel surcharge (configuration section <c>HeavyParcelSurcharge</c>).</summary>
public sealed class HeavyParcelSurchargeOptions
{
    public const string SectionName = "HeavyParcelSurcharge";

    /// <summary>The bands used when configuration supplies none: over 30 kg pays 15.00, over 50 kg pays 25.00.</summary>
    public static readonly IReadOnlyList<SurchargeTier> DefaultTiers = [new(30m, 15.00m), new(50m, 25.00m)];

    /// <summary>Shipments booked on or after this instant pay the surcharge.</summary>
    public DateTimeOffset EffectiveFromUtc { get; set; } = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Surcharge bands. Empty means <see cref="DefaultTiers"/>. (Deliberately not pre-filled: the configuration binder
    /// appends to a pre-filled list instead of replacing it, which would silently mix defaults with configured bands.)
    /// </summary>
    public List<SurchargeTier> Tiers { get; set; } = [];

    /// <summary>The bands in force: the configured ones, or the defaults.</summary>
    public IReadOnlyList<SurchargeTier> EffectiveTiers => Tiers.Count > 0 ? Tiers : DefaultTiers;

    /// <summary>Start-up check: every band starts inside the service range, charges a positive amount, and no two bands start at the same weight.</summary>
    public bool IsValid() =>
        EffectiveTiers.All(t => t.OverKg >= TrackingLimits.MinWeightKg && t.OverKg < TrackingLimits.MaxWeightKg && t.Amount > 0m)
        && EffectiveTiers.Select(t => t.OverKg).Distinct().Count() == EffectiveTiers.Count;
}

/// <summary>What a shipment is charged on top of the base price.</summary>
public sealed record ShipmentChargesDto(string TrackingNumber, decimal WeightKg, decimal HeavyParcelSurcharge);

/// <summary>
/// "Shipments over 30 kg require a heavy-parcel surcharge" (TDD-LOG.md). Table-driven: the bands and the effective date
/// come from <see cref="HeavyParcelSurchargeOptions"/>, and "now" comes from the injected <see cref="TimeProvider"/>,
/// never from <c>DateTime.Now</c>, so every rule is testable with a fixed clock.
/// </summary>
public sealed class HeavyParcelSurchargePolicy : IShipmentSurchargePolicy
{
    private readonly DateTimeOffset _effectiveFromUtc;
    private readonly SurchargeTier[] _heaviestFirst;
    private readonly TimeProvider _clock;

    public HeavyParcelSurchargePolicy(IOptions<HeavyParcelSurchargeOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        _effectiveFromUtc = options.Value.EffectiveFromUtc;
        _heaviestFirst = [.. options.Value.EffectiveTiers.OrderByDescending(t => t.OverKg)];
        _clock = clock;
    }

    public decimal SurchargeFor(decimal weightKg, DateTimeOffset bookedAtUtc)
    {
        TrackingLimits.EnsureWeightInRange(weightKg, nameof(weightKg));
        if (bookedAtUtc < _effectiveFromUtc)
        {
            return 0m;
        }

        return Array.Find(_heaviestFirst, tier => weightKg > tier.OverKg)?.Amount ?? 0m;
    }

    public decimal QuoteNow(decimal weightKg) => SurchargeFor(weightKg, _clock.GetUtcNow());
}
