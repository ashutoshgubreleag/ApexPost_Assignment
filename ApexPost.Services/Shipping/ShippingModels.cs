using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Models.Validation;
using ApexPost.Models.ValueObjects;

namespace ApexPost.Services.Shipping;

/// <summary>Input for a shipping quote. Attribute rules are checked by <see cref="ReflectionValidator"/>.</summary>
public sealed record ShippingQuoteCommand
{
    [DefinedEnum]
    public ShippingType ShippingType { get; init; } = ShippingType.Parcel;

    [DefinedEnum]
    public ShippingServiceLevel ServiceLevel { get; init; } = ShippingServiceLevel.Standard;

    [NotBlank, Matches("^Z[0-9]{2}$", "a zone code such as Z01")]
    public string ZoneCode { get; init; } = string.Empty;

    [InRange(0.001, 100)]
    public decimal WeightKg { get; init; }

    [InRange(0.1, 300)]
    public decimal LengthCm { get; init; }

    [InRange(0.1, 300)]
    public decimal WidthCm { get; init; }

    [InRange(0.1, 300)]
    public decimal HeightCm { get; init; }
}

/// <summary>Recipient details for the label.</summary>
public sealed record RecipientCommand
{
    [NotBlank, MaxLengthRule(80)] public string RecipientName { get; init; } = string.Empty;
    [NotBlank, MaxLengthRule(120)] public string Line1 { get; init; } = string.Empty;
    [NotBlank, MaxLengthRule(60)] public string City { get; init; } = string.Empty;
    [NotBlank, MaxLengthRule(20)] public string State { get; init; } = string.Empty;
    [NotBlank, MaxLengthRule(12)] public string Postcode { get; init; } = string.Empty;

    public Address ToAddress() => new(RecipientName, Line1, City, State, Postcode);
}

public sealed record SurchargeLine(string Name, Money Amount);

/// <summary>Everything a pricing strategy needs, including the live fuel surcharge rate from the courier.</summary>
public sealed record ShippingPricingContext(
    ShippingType ShippingType,
    ShippingServiceLevel ServiceLevel,
    ZoneDto Zone,
    Weight ActualWeight,
    Dimensions Dimensions,
    decimal FuelSurchargeRate);

/// <summary>A priced shipment. <see cref="Net"/> is tax-exclusive.</summary>
public sealed record ShippingQuote(
    ShippingPricingContext Context,
    Weight ChargeableWeight,
    Money BasePostage,
    Money ServiceLoading,
    IReadOnlyList<SurchargeLine> Surcharges,
    Money Net,
    TaxCategory TaxCategory,
    Money Tax)
{
    public Money Total => Net + Tax;
}

/// <summary>A configurable pricing add-on. Returns null when it does not apply.</summary>
public delegate SurchargeLine? ShippingSurchargeRule(ShippingPricingContext context);
