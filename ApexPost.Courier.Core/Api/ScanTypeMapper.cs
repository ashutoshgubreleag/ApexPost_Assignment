using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Core.Api;

/// <summary>Maps the courier's four on-device scan kinds to the server's six. Arrived / OutForDelivery are depot events the app never records.</summary>
public static class ScanTypeMapper
{
    public static ApiScanType ToApi(ScanType type) => type switch
    {
        ScanType.Pickup => ApiScanType.PickedUp,
        ScanType.InTransit => ApiScanType.Departed,
        ScanType.Delivered => ApiScanType.Delivered,
        ScanType.Failed => ApiScanType.DeliveryFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown scan type."),
    };
}
