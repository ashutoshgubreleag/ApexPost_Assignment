namespace ApexPost.Courier.Offline.Domain;

/// <summary>What the courier did with the parcel at the moment of the scan.</summary>
public enum ScanType
{
    Pickup,
    InTransit,
    Delivered,
    Failed,
}

/// <summary>Where a scan is in the offline outbox. New scans always start as <see cref="Pending"/>.</summary>
public enum SyncStatus
{
    Pending,
    Synced,
    Failed,
}
