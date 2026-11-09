namespace ApexPost.Courier.Offline.Domain;

/// <summary>
/// Persistence port for scans. The local store is embedded and fast, so the operations are synchronous;
/// the outbox (the only async caller) wraps them. Implementations must never leak storage types.
/// </summary>
public interface IScanRepository
{
    /// <summary>Saves a new scan. Whatever status the caller passes, the stored scan is <see cref="SyncStatus.Pending"/>.</summary>
    ShipmentScan Create(ShipmentScan scan);

    /// <summary>Key-value lookup by scan id (no query involved). Null when the scan does not exist.</summary>
    ShipmentScan? Get(Guid scanId);

    /// <summary>Replaces the stored fields of an existing scan. False when it does not exist.</summary>
    bool Update(ShipmentScan scan);

    /// <summary>Deletes a scan. False when it does not exist.</summary>
    bool Delete(Guid scanId);

    IReadOnlyList<ShipmentScan> ListAll();

    /// <summary>All scans for a tracking number, oldest first (ordered by scannedAtUtc).</summary>
    IReadOnlyList<ShipmentScan> GetByTrackingNumber(string trackingNumber);

    /// <summary>Scans still waiting to be uploaded (syncStatus = Pending), oldest first; optionally only the first <paramref name="take"/>.</summary>
    IReadOnlyList<ShipmentScan> GetPending(int? take = null);

    int CountPending();

    /// <summary>Number of scans per scan type for one courier. Every <see cref="ScanType"/> is present (0 when none).</summary>
    IReadOnlyDictionary<ScanType, int> CountByScanType(string courierId);
}
