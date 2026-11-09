namespace ApexPost.Courier.Offline.Domain;

/// <summary>
/// One shipment scan recorded on the courier's device. A plain domain record: it knows nothing about
/// Couchbase Lite (the infrastructure layer maps it to and from a JSON document).
/// </summary>
public sealed record ShipmentScan(
    Guid ScanId,
    string TrackingNumber,
    ScanType ScanType,
    string CourierId,
    string Location,
    DateTimeOffset ScannedAtUtc,
    string? Notes,
    SyncStatus SyncStatus)
{
    /// <summary>The Couchbase document id, <c>scan::{Guid}</c>.</summary>
    public string DocumentId => ScanIds.ToDocumentId(ScanId);

    /// <summary>Creates a brand-new scan: fresh id, and always <see cref="SyncStatus.Pending"/> (saved locally first).</summary>
    public static ShipmentScan CreateNew(
        string trackingNumber,
        ScanType scanType,
        string courierId,
        string location,
        DateTimeOffset scannedAtUtc,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackingNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(courierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        return new ShipmentScan(
            Guid.NewGuid(),
            trackingNumber.Trim(),
            scanType,
            courierId.Trim(),
            location.Trim(),
            scannedAtUtc.ToUniversalTime(),
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            SyncStatus.Pending);
    }
}
