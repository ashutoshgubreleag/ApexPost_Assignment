
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;

namespace ApexPost.Models.Domain;

/// <summary>Service-level limits expressed with tuple and relational patterns.</summary>
public static class ServiceLevelRules
{
    public static Weight MaxWeightFor(ShippingType type, ShippingServiceLevel level) => (type, level) switch
    {
        (ShippingType.Letter, _) => new Weight(0.5m),
        (ShippingType.Parcel, ShippingServiceLevel.Standard) => new Weight(22m),
        (ShippingType.Parcel, ShippingServiceLevel.Express) => new Weight(20m),
        (ShippingType.Parcel, ShippingServiceLevel.Registered) => new Weight(5m),
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown service level."),
    };

    /// <summary>Rejects shipments the selected service cannot carry.</summary>
    public static void EnsureAcceptable(ShippingType type, ShippingServiceLevel level, Weight weight, Dimensions dimensions)
    {
        var max = MaxWeightFor(type, level);
        if (weight > max)
        {
            throw new BusinessRuleViolationException(
                "PARCEL_OVERWEIGHT",
                $"{type} weighing {weight} exceeds the {max} limit for {level} service.");
        }

        // A letter must actually be letter-shaped.
        if (type == ShippingType.Letter && dimensions is not { HeightCm: <= 2m, LongestSideCm: <= 36m })
        {
            throw new BusinessRuleViolationException(
                "LETTER_OVERSIZE",
                "Letters must be at most 36 cm long and 2 cm thick; send this as a parcel.");
        }

        if (type == ShippingType.Parcel && dimensions.LongestSideCm > 150m)
        {
            throw new BusinessRuleViolationException("PARCEL_OVERSIZE", "Parcels longer than 150 cm cannot be accepted at the counter.");
        }
    }
}
