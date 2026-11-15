using Microsoft.Extensions.Options;

namespace ApexPost.Services.Tracking;

/// <summary>Decides the heavy-parcel surcharge for a tracked shipment (Week 5 TDD rule, see TDD-LOG.md).</summary>
public interface IShipmentSurchargePolicy
{
    /// <summary>The surcharge for a shipment of <paramref name="weightKg"/> booked at <paramref name="bookedAtUtc"/>.</summary>
    decimal SurchargeFor(decimal weightKg, DateTimeOffset bookedAtUtc);

    /// <summary>The surcharge for a shipment of <paramref name="weightKg"/> booked now (the injected clock's "now").</summary>
    decimal QuoteNow(decimal weightKg);
}

/// <summary>Settings of the heavy-parcel surcharge (section <c>HeavyParcelSurcharge</c>).</summary>
public sealed class HeavyParcelSurchargeOptions
{
    public const string SectionName = "HeavyParcelSurcharge";

    /// <summary>Shipments booked on or after this instant pay the surcharge.</summary>
    public DateTimeOffset EffectiveFromUtc { get; set; } = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
}

/// <summary>What a shipment is charged on top of the base price.</summary>
public sealed record ShipmentChargesDto(string TrackingNumber, decimal WeightKg, decimal HeavyParcelSurcharge);

/// <summary>RED: signature only, so the tests compile and fail. Implemented test-first (see TDD-LOG.md).</summary>
public sealed class HeavyParcelSurchargePolicy : IShipmentSurchargePolicy
{
    public HeavyParcelSurchargePolicy(IOptions<HeavyParcelSurchargeOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
    }

    public decimal SurchargeFor(decimal weightKg, DateTimeOffset bookedAtUtc) => throw new NotImplementedException();

    public decimal QuoteNow(decimal weightKg) => throw new NotImplementedException();
}
