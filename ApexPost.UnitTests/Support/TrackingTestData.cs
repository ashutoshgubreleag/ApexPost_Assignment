using System.Reflection;
using ApexPost.Models.Domain;
using ApexPost.Services.Tracking;

namespace ApexPost.UnitTests.Support;

/// <summary>Fictional, fixed test data for the tracking domain. No wall clock, no random values in assertions.</summary>
internal static class TrackingTestData
{
    public const string TrackingNumber = "APX-10000001";

    /// <summary>The fixed "now" of every test clock.</summary>
    public static readonly DateTimeOffset Now = new(2026, 1, 15, 8, 0, 0, TimeSpan.Zero);

    public static ShipmentDetails ValidDetails(decimal weightKg = 2.5m) =>
        new("Test Recipient", "+61 400 000 111", "1 Test Street", "Melbourne", weightKg);

    public static CreateTrackedShipmentCommand ValidCommand(string trackingNumber = TrackingNumber, decimal weightKg = 2.5m) =>
        new(trackingNumber, ValidDetails(weightKg));

    public static TrackedShipmentDto Dto(string trackingNumber = TrackingNumber, TrackingStatus status = TrackingStatus.Created) =>
        new(trackingNumber, "Test Recipient", "+61 400 000 111", "1 Test Street", "Melbourne", 2.5m, status, Now, null);

    /// <summary>A shipment as the repository would load it: persisted (non-zero id) and in the requested status.</summary>
    public static TrackedShipment Shipment(TrackingStatus status = TrackingStatus.Created, int id = 7)
    {
        var shipment = TrackedShipment.Create(TrackingNumber, "Test Recipient", "+61 400 000 111", "1 Test Street", "Melbourne", 2.5m, Now);
        foreach (var step in PathTo(status))
        {
            shipment.ChangeStatus(step);
        }

        // Entity.Id has a protected setter (EF Core assigns it); set it the same way to simulate a loaded row.
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(shipment, id);
        return shipment;
    }

    public static ScanInput Scan(ScanType type = ScanType.Arrived, Guid? scanId = null, int minutesAfterNow = -60, string? signature = null) =>
        new(scanId ?? Guid.NewGuid(), type, "Melbourne Depot", Now.AddMinutes(minutesAfterNow), signature);

    /// <summary>The legal forward path from Created to <paramref name="target"/>.</summary>
    public static IEnumerable<TrackingStatus> PathTo(TrackingStatus target) => target switch
    {
        TrackingStatus.Created => [],
        TrackingStatus.PickedUp => [TrackingStatus.PickedUp],
        TrackingStatus.InTransit => [TrackingStatus.PickedUp, TrackingStatus.InTransit],
        TrackingStatus.OutForDelivery => [TrackingStatus.PickedUp, TrackingStatus.InTransit, TrackingStatus.OutForDelivery],
        TrackingStatus.Delivered => [TrackingStatus.PickedUp, TrackingStatus.InTransit, TrackingStatus.OutForDelivery, TrackingStatus.Delivered],
        TrackingStatus.Failed => [TrackingStatus.PickedUp, TrackingStatus.InTransit, TrackingStatus.OutForDelivery, TrackingStatus.Failed],
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };
}
