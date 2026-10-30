using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Common;

namespace ApexPost.Services.Shipping;

/// <summary>
/// Zone + weight pricing:
/// <list type="number">
/// <item>Chargeable weight = actual weight, or volumetric weight for parcels if larger.</item>
/// <item>Base postage from the zone rate card (pattern match on type and weight band).</item>
/// <item>Service loading by service level (pattern match).</item>
/// <item>Surcharges from a list of <see cref="ShippingSurchargeRule"/> delegates (fuel, remote area, oversize).</item>
/// <item>Tax: domestic Standard, international Exempt.</item>
/// </list>
/// </summary>
public sealed class DefaultShippingPricingStrategy : IShippingPricingStrategy
{
    private readonly IReadOnlyList<ShippingSurchargeRule> _surcharges;

    public DefaultShippingPricingStrategy() : this(DefaultSurcharges) { }

    public DefaultShippingPricingStrategy(IReadOnlyList<ShippingSurchargeRule> surcharges) => _surcharges = surcharges;

    /// <summary>The standard surcharge rules. Each is a plain delegate, so adding one is a one-liner.</summary>
    public static IReadOnlyList<ShippingSurchargeRule> DefaultSurcharges { get; } =
    [
        ctx => ctx.FuelSurchargeRate > 0
            ? new SurchargeLine("Fuel surcharge", BaseFor(ctx, ChargeableWeightFor(ctx)).ApplyRate(ctx.FuelSurchargeRate))
            : null,
        ctx => ctx.Zone.IsRemote ? new SurchargeLine("Remote area", new Money(3.00m)) : null,
        ctx => ctx is { ShippingType: ShippingType.Parcel, Dimensions.LongestSideCm: > 100m }
            ? new SurchargeLine("Oversize handling", new Money(12.00m))
            : null,
    ];

    public ShippingQuote Price(ShippingPricingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var chargeable = ChargeableWeightFor(context);
        var basePostage = BaseFor(context, chargeable);

        var serviceLoading = context.ServiceLevel switch
        {
            ShippingServiceLevel.Standard => Money.Zero,
            ShippingServiceLevel.Express => basePostage.ApplyRate(0.60m),
            ShippingServiceLevel.Registered => new Money(4.50m), // signature on delivery
            _ => throw new ArgumentOutOfRangeException(nameof(context), context.ServiceLevel, "Unknown service level."),
        };

        var surcharges = _surcharges
            .Select(rule => rule(context))
            .OfType<SurchargeLine>()
            .ToList();

        var net = (basePostage + serviceLoading + surcharges.Select(s => s.Amount).Total()).RoundedToCents;
        var taxCategory = TaxRules.CategoryFor(TransactionLineType.Shipping, isInternational: context.Zone.IsInternational);

        return new ShippingQuote(context, chargeable, basePostage, serviceLoading, surcharges, net, taxCategory, TaxRules.TaxOn(net, taxCategory));
    }

    private static Weight ChargeableWeightFor(ShippingPricingContext context) => context.ShippingType switch
    {
        ShippingType.Parcel => Weight.Max(context.ActualWeight, context.Dimensions.VolumetricWeight),
        _ => context.ActualWeight,
    };

    private static Money BaseFor(ShippingPricingContext context, Weight chargeable) =>
        (context.ShippingType, chargeable.Kilograms) switch
        {
            (ShippingType.Letter, <= 0.25m) => context.Zone.LetterBaseRate,
            (ShippingType.Letter, _) => context.Zone.LetterBaseRate * 2,                 // large letter
            (ShippingType.Parcel, <= 0.5m) => context.Zone.ParcelBaseRate,              // satchel band
            (ShippingType.Parcel, var kg) => context.Zone.ParcelBaseRate + context.Zone.PerKgRate * Math.Ceiling(kg - 0.5m),
            _ => throw new ArgumentOutOfRangeException(nameof(context), context.ShippingType, "Unknown shipping type."),
        };
}
