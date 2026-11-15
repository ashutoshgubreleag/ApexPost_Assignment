using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Tracking;
using ServerScanType = ApexPost.Models.Domain.ScanType;

namespace ApexPost.Specs.Support;

/// <summary>The courier phone's scan store, in memory (the Couchbase Lite store has its own tests on a real database).</summary>
public sealed class InMemoryScanRepository : IScanRepository
{
    private readonly Dictionary<Guid, ShipmentScan> _items = [];

    public ShipmentScan Create(ShipmentScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var pending = scan with { SyncStatus = SyncStatus.Pending };
        _items.Add(scan.ScanId, pending);
        return pending;
    }

    public ShipmentScan? Get(Guid scanId) => _items.GetValueOrDefault(scanId);

    public bool Update(ShipmentScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (!_items.ContainsKey(scan.ScanId))
        {
            return false;
        }

        _items[scan.ScanId] = scan;
        return true;
    }

    public bool Delete(Guid scanId) => _items.Remove(scanId);

    public IReadOnlyList<ShipmentScan> ListAll() => [.. _items.Values];

    public IReadOnlyList<ShipmentScan> GetByTrackingNumber(string trackingNumber) =>
        [.. _items.Values.Where(s => s.TrackingNumber == trackingNumber).OrderBy(s => s.ScannedAtUtc)];

    public IReadOnlyList<ShipmentScan> GetPending(int? take = null)
    {
        var pending = _items.Values.Where(s => s.SyncStatus == SyncStatus.Pending).OrderBy(s => s.ScannedAtUtc);
        return [.. take is { } n ? pending.Take(n) : pending];
    }

    public int CountPending() => _items.Values.Count(s => s.SyncStatus == SyncStatus.Pending);

    public IReadOnlyDictionary<ScanType, int> CountByScanType(string courierId) =>
        Enum.GetValues<ScanType>().ToDictionary(t => t, t => _items.Values.Count(s => s.CourierId == courierId && s.ScanType == t));

    public void Clear() => _items.Clear();
}

/// <summary>
/// Connects the phone's outbox straight to the server's business service, in-process (no HTTP, no network).
/// It follows the same rules as the real <c>ApiShipmentUploader</c>: one request per tracking number, the production
/// <see cref="ScanTypeMapper"/>, the signature taken from Notes, accepted or duplicate means success, a rejection
/// means failure, and an unreachable server throws <see cref="UploaderUnavailableException"/> so scans stay Pending.
/// </summary>
public sealed class InProcessUploader(Func<IShipmentTrackingService> server) : IShipmentUploader
{
    /// <summary>When false, every upload fails as if the phone had no connection.</summary>
    public bool ServerReachable { get; set; } = true;

    public int Requests { get; private set; }

    /// <summary>Every outcome the server gave, by scan id (the phone only keeps Synced/Failed, not the reason).</summary>
    public Dictionary<Guid, ScanUploadOutcome> Outcomes { get; } = [];

    public async Task<IReadOnlyList<ScanUploadOutcome>> UploadAsync(IReadOnlyList<ShipmentScan> batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!ServerReachable)
        {
            throw new UploaderUnavailableException("No connection to the tracking system.");
        }

        var outcomes = new List<ScanUploadOutcome>();
        foreach (var group in batch.GroupBy(s => s.TrackingNumber, StringComparer.Ordinal))
        {
            Requests++;
            var result = await server().UploadScansAsync(group.Key, [.. group.Select(ToServer)], cancellationToken);
            var rejected = result.Rejected.ToDictionary(r => r.ScanId, r => r.Reason);
            outcomes.AddRange(group.Select(s => rejected.TryGetValue(s.ScanId, out var reason)
                ? new ScanUploadOutcome(s.ScanId, false, reason)
                : new ScanUploadOutcome(s.ScanId, true)));
        }

        foreach (var outcome in outcomes)
        {
            Outcomes[outcome.ScanId] = outcome;
        }

        return outcomes;
    }

    /// <summary>Phone scan to server scan, using the production mapping (the enum names match the API contract).</summary>
    public static ScanInput ToServer(ShipmentScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var type = Enum.Parse<ServerScanType>(ScanTypeMapper.ToApi(scan.ScanType).ToString());
        return new ScanInput(scan.ScanId, type, scan.Location, scan.ScannedAtUtc, scan.ScanType == ScanType.Delivered ? scan.Notes : null);
    }
}
