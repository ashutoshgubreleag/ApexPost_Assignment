using System.Globalization;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;

namespace ApexPost.Models.Domain;

/// <summary>A destination zone. Its rate card lives in <see cref="ShippingRate"/> rows (one per shipping type).</summary>
public sealed class ShippingZone : Entity
{
    private readonly List<ShippingRate> _rates = [];

    private ShippingZone()
    {
        // For EF Core materialisation.
    }

    public string ZoneCode { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsInternational { get; private set; }

    public bool IsRemote { get; private set; }

    public IReadOnlyCollection<ShippingRate> Rates => _rates.AsReadOnly();

    public Money LetterBaseRate => RateFor(ShippingType.Letter).BaseRate;

    public Money ParcelBaseRate => RateFor(ShippingType.Parcel).BaseRate;

    public Money PerKgRate => RateFor(ShippingType.Parcel).PerKgRate;

    public static ShippingZone Create(string zoneCode, string name, bool isInternational, bool isRemote, Money letterBaseRate, Money parcelBaseRate, Money perKgRate)
    {
        var code = Guard.Text(zoneCode, nameof(zoneCode), 5).ToUpperInvariant();
        var zone = new ShippingZone
        {
            ZoneCode = code,
            Name = Guard.Text(name, nameof(name), 60),
            IsInternational = isInternational,
            IsRemote = isRemote,
        };
        zone._rates.Add(new ShippingRate(code, ShippingType.Letter, letterBaseRate, Money.Zero));
        zone._rates.Add(new ShippingRate(code, ShippingType.Parcel, parcelBaseRate, perKgRate));
        return zone;
    }

    public ShippingRate RateFor(ShippingType type) =>
        _rates.FirstOrDefault(r => r.ShippingType == type)
        ?? throw new BusinessRuleViolationException("RATE_MISSING", $"Zone {ZoneCode} has no {type} rate.");
}

/// <summary>Rate card row: zone x shipping type. Kept separate from the zone (3NF: a rate depends on both).</summary>
public sealed class ShippingRate : Entity
{
    private ShippingRate()
    {
        // For EF Core materialisation.
    }

    internal ShippingRate(string zoneCode, ShippingType type, Money baseRate, Money perKgRate)
    {
        ZoneCode = zoneCode;
        ShippingType = type;
        Revise(baseRate, perKgRate);
    }

    public string ZoneCode { get; private set; } = string.Empty;

    public ShippingType ShippingType { get; private set; }

    public Money BaseRate { get; private set; }

    public Money PerKgRate { get; private set; }

    public void Revise(Money baseRate, Money perKgRate)
    {
        if (baseRate <= Money.Zero || perKgRate < Money.Zero)
        {
            throw new BusinessRuleViolationException("RATE_INVALID", "Base rate must be positive and the per-kg rate cannot be negative.");
        }

        BaseRate = baseRate;
        PerKgRate = perKgRate;
    }
}

/// <summary>A letter or parcel lodged as part of a counter transaction.</summary>
public sealed class Shipment : Entity
{
    private Shipment()
    {
        // For EF Core materialisation.
    }

    public string TrackingNumber { get; private set; } = string.Empty;

    public int TransactionId { get; private set; }

    public BranchCode BranchCode { get; private set; }

    public ShippingType ShippingType { get; private set; }

    public ShippingServiceLevel ServiceLevel { get; private set; }

    public string ZoneCode { get; private set; } = string.Empty;

    public Weight Weight { get; private set; }

    public Dimensions Dimensions { get; private set; }

    public Weight ChargeableWeight { get; private set; }

    public Address? Recipient { get; private set; }

    /// <summary>Tax-exclusive postage.</summary>
    public Money Price { get; private set; }

    public ShipmentStatus Status { get; private set; } = ShipmentStatus.Quoted;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates a quoted shipment. Rule: the weight and size must suit the service level.</summary>
    public static Shipment Create(
        int transactionId,
        BranchCode branch,
        ShippingType type,
        ShippingServiceLevel level,
        string zoneCode,
        Weight weight,
        Dimensions dimensions,
        Weight chargeableWeight,
        Address recipient,
        Money price,
        DateTimeOffset at,
        int trackingSuffix)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ServiceLevelRules.EnsureAcceptable(type, level, weight, dimensions);
        return new Shipment
        {
            TrackingNumber = string.Create(CultureInfo.InvariantCulture, $"APX{at:yyMMdd}{trackingSuffix:000000}"),
            TransactionId = transactionId,
            BranchCode = branch,
            ShippingType = type,
            ServiceLevel = level,
            ZoneCode = zoneCode,
            Weight = weight,
            Dimensions = dimensions,
            ChargeableWeight = chargeableWeight,
            Recipient = recipient,
            Price = price,
            Status = ShipmentStatus.Quoted,
            CreatedAt = at,
        };
    }

    public void Lodge()
    {
        if (Status == ShipmentStatus.Quoted)
        {
            Status = ShipmentStatus.Lodged;
        }
    }

    public void Cancel() => Status = ShipmentStatus.Cancelled;
}
