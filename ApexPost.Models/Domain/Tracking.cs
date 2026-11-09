using ApexPost.Models.Exceptions;

namespace ApexPost.Models.Domain;

/// <summary>Lifecycle of a tracked courier shipment. Moves forward only (see <see cref="TrackedShipment.CanMoveTo"/>).</summary>
public enum TrackingStatus
{
    Created,
    PickedUp,
    InTransit,
    OutForDelivery,
    Delivered,
    Failed
}

/// <summary>What a depot or courier scanner recorded.</summary>
public enum ScanType
{
    PickedUp,
    Arrived,
    Departed,
    OutForDelivery,
    Delivered,
    DeliveryFailed
}

/// <summary>
/// A shipment as seen by the courier tracking API. Separate from the counter-sale
/// <see cref="Shipment"/> aggregate on purpose: it owns its own tracking number and status machine.
/// </summary>
public sealed class TrackedShipment : Entity
{
    public const int MaxText = 200;

    private static readonly Dictionary<TrackingStatus, TrackingStatus[]> Transitions = new()
    {
        [TrackingStatus.Created] = [TrackingStatus.PickedUp],
        [TrackingStatus.PickedUp] = [TrackingStatus.InTransit],
        [TrackingStatus.InTransit] = [TrackingStatus.OutForDelivery],
        [TrackingStatus.OutForDelivery] = [TrackingStatus.Delivered, TrackingStatus.Failed],
        [TrackingStatus.Delivered] = [],
        [TrackingStatus.Failed] = []
    };

    private readonly List<ShipmentScan> _scans = [];

    private TrackedShipment() { }

    public string TrackingNumber { get; private set; } = string.Empty;
    public string RecipientName { get; private set; } = string.Empty;
    public string RecipientPhone { get; private set; } = string.Empty;
    public string AddressLine { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public decimal WeightKg { get; private set; }
    public TrackingStatus Status { get; private set; } = TrackingStatus.Created;
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<ShipmentScan> Scans => _scans;

    public static TrackedShipment Create(string trackingNumber, string recipientName, string recipientPhone,
        string addressLine, string city, decimal weightKg, DateTimeOffset now)
    {
        var shipment = new TrackedShipment
        {
            TrackingNumber = Guard.Text(trackingNumber, nameof(trackingNumber), 20),
            CreatedAt = now
        };
        shipment.Update(recipientName, recipientPhone, addressLine, city, weightKg);
        return shipment;
    }

    /// <summary>Replaces the editable details. Allowed only while the shipment has not been picked up.</summary>
    public void Update(string recipientName, string recipientPhone, string addressLine, string city, decimal weightKg)
    {
        if (!IsTransient && Status != TrackingStatus.Created)
        {
            throw new ConflictException("SHIPMENT_LOCKED", $"Shipment {TrackingNumber} is {Status} and can no longer be edited.");
        }

        if (weightKg is < 0.1m or > 70m)
        {
            throw new ArgumentOutOfRangeException(nameof(weightKg), weightKg, "Weight must be between 0.1 and 70 kg.");
        }

        RecipientName = Guard.Text(recipientName, nameof(recipientName), MaxText);
        RecipientPhone = Guard.Text(recipientPhone, nameof(recipientPhone), 20);
        AddressLine = Guard.Text(addressLine, nameof(addressLine), MaxText);
        City = Guard.Text(city, nameof(city), 100);
        WeightKg = weightKg;
    }

    public static bool CanMoveTo(TrackingStatus from, TrackingStatus to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>Moves to <paramref name="next"/>, or throws <see cref="ConflictException"/> (HTTP 409) if the move is not allowed.</summary>
    public void ChangeStatus(TrackingStatus next)
    {
        Guard.Defined(next, nameof(next));
        if (!CanMoveTo(Status, next))
        {
            throw new ConflictException("INVALID_STATUS_TRANSITION", $"Cannot move shipment {TrackingNumber} from {Status} to {next}.");
        }

        Status = next;
    }

    /// <summary>Deletion is allowed only before the courier has touched the parcel.</summary>
    public void EnsureDeletable()
    {
        if (Status != TrackingStatus.Created)
        {
            throw new ConflictException("SHIPMENT_NOT_DELETABLE", $"Shipment {TrackingNumber} is {Status}; only Created shipments can be deleted.");
        }
    }

    /// <summary>The status a scan implies, or null when the scan does not change the status.</summary>
    public static TrackingStatus? StatusFor(ScanType type) => type switch
    {
        ScanType.PickedUp => TrackingStatus.PickedUp,
        ScanType.Arrived or ScanType.Departed => TrackingStatus.InTransit,
        ScanType.OutForDelivery => TrackingStatus.OutForDelivery,
        ScanType.Delivered => TrackingStatus.Delivered,
        ScanType.DeliveryFailed => TrackingStatus.Failed,
        _ => null
    };

    /// <summary>
    /// Records a scan. It does not change the status: call <see cref="AdvanceFor"/> for that, so a
    /// scan that arrives out of order is stored without ever moving the shipment backwards.
    /// </summary>
    public ShipmentScan AddScan(Guid scanId, ScanType type, string location, DateTimeOffset scannedAt, string? signatureName)
    {
        var scan = new ShipmentScan(scanId, type, location, scannedAt, signatureName);
        _scans.Add(scan);
        return scan;
    }

    /// <summary>Applies the status implied by <paramref name="type"/> if it is a legal forward move; returns whether it changed.</summary>
    public bool AdvanceFor(ScanType type)
    {
        var target = StatusFor(type);
        if (target is null || !CanMoveTo(Status, target.Value))
        {
            return false;
        }

        Status = target.Value;
        return true;
    }
}

/// <summary>One scanner event. <see cref="ScanId"/> is the client-generated idempotency key (unique in the database).</summary>
public sealed class ShipmentScan : Entity
{
    private ShipmentScan() { }

    internal ShipmentScan(Guid scanId, ScanType type, string location, DateTimeOffset scannedAt, string? signatureName)
    {
        if (scanId == Guid.Empty)
        {
            throw new ArgumentException("ScanId is required.", nameof(scanId));
        }

        ScanId = scanId;
        Type = Guard.Defined(type, nameof(type));
        Location = Guard.Text(location, nameof(location), 100);
        ScannedAt = scannedAt;
        RecipientSignatureName = string.IsNullOrWhiteSpace(signatureName) ? null : Guard.Text(signatureName, nameof(signatureName), 100);
    }

    public int ShipmentId { get; private set; }
    public Guid ScanId { get; private set; }
    public ScanType Type { get; private set; }
    public string Location { get; private set; } = string.Empty;
    public DateTimeOffset ScannedAt { get; private set; }
    public string? RecipientSignatureName { get; private set; }
}
