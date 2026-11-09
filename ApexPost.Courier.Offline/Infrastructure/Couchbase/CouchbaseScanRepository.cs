using System.Globalization;
using ApexPost.Courier.Offline.Domain;
using Couchbase.Lite;
using Couchbase.Lite.Query;

namespace ApexPost.Courier.Offline.Infrastructure.Couchbase;

/*
 * Couchbase Lite implementation of IScanRepository. A scan is a JSON document with id scan::{Guid}.
 * Enums are stored as strings and times as ISO-8601 UTC strings, so they sort and compare correctly in SQL++.
 * Every SQL++ query is parameterized ($name): user text is never concatenated into a query string.
 */
public sealed class CouchbaseScanRepository(CourierDatabase db) : IScanRepository
{
    private const string Source = CourierDatabase.QuerySource;

    private const string ByTrackingSql =
        $"SELECT META().id AS id FROM {Source} WHERE type = 'scan' AND trackingNumber = $trackingNumber ORDER BY scannedAtUtc";

    private const string PendingSql =
        $"SELECT META().id AS id FROM {Source} WHERE type = 'scan' AND syncStatus = 'Pending' ORDER BY scannedAtUtc";

    private const string PendingLimitedSql =
        $"SELECT META().id AS id FROM {Source} WHERE type = 'scan' AND syncStatus = 'Pending' ORDER BY scannedAtUtc LIMIT $take";

    private const string AllSql =
        $"SELECT META().id AS id FROM {Source} WHERE type = 'scan' ORDER BY scannedAtUtc";

    private const string CountPendingSql =
        $"SELECT COUNT(*) AS total FROM {Source} WHERE type = 'scan' AND syncStatus = 'Pending'";

    private const string CountByTypeSql =
        $"SELECT scanType, COUNT(*) AS total FROM {Source} WHERE type = 'scan' AND courierId = $courierId GROUP BY scanType";

    public ShipmentScan Create(ShipmentScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var pending = scan with { SyncStatus = SyncStatus.Pending };

        using var existing = db.Scans.GetDocument(pending.DocumentId);
        if (existing is not null)
        {
            throw new InvalidOperationException($"Scan {pending.ScanId} already exists.");
        }

        using var doc = new MutableDocument(pending.DocumentId);
        Write(doc, pending);
        db.Scans.Save(doc);
        return pending;
    }

    public ShipmentScan? Get(Guid scanId)
    {
        using var doc = db.Scans.GetDocument(ScanIds.ToDocumentId(scanId));
        return doc is null ? null : Map(doc);
    }

    public bool Update(ShipmentScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        using var existing = db.Scans.GetDocument(scan.DocumentId);
        if (existing is null)
        {
            return false;
        }

        using var doc = existing.ToMutable();
        Write(doc, scan);
        db.Scans.Save(doc);
        return true;
    }

    public bool Delete(Guid scanId)
    {
        using var doc = db.Scans.GetDocument(ScanIds.ToDocumentId(scanId));
        if (doc is null)
        {
            return false;
        }

        db.Scans.Delete(doc);
        return true;
    }

    public IReadOnlyList<ShipmentScan> ListAll()
    {
        using var query = db.CreateQuery(AllSql);
        return Load(query);
    }

    public IReadOnlyList<ShipmentScan> GetByTrackingNumber(string trackingNumber)
    {
        ArgumentNullException.ThrowIfNull(trackingNumber);
        using var query = db.CreateQuery(ByTrackingSql);
        query.Parameters = new Parameters().SetString("trackingNumber", trackingNumber.Trim());
        return Load(query);
    }

    public IReadOnlyList<ShipmentScan> GetPending(int? take = null)
    {
        if (take is null)
        {
            using var all = db.CreateQuery(PendingSql);
            return Load(all);
        }

        using var limited = db.CreateQuery(PendingLimitedSql);
        limited.Parameters = new Parameters().SetInt("take", take.Value);
        return Load(limited);
    }

    public int CountPending()
    {
        using var query = db.CreateQuery(CountPendingSql);
        return query.Execute().AllResults().FirstOrDefault()?.GetInt("total") ?? 0;
    }

    public IReadOnlyDictionary<ScanType, int> CountByScanType(string courierId)
    {
        ArgumentNullException.ThrowIfNull(courierId);
        var counts = Enum.GetValues<ScanType>().ToDictionary(type => type, _ => 0);

        using var query = db.CreateQuery(CountByTypeSql);
        query.Parameters = new Parameters().SetString("courierId", courierId.Trim());
        foreach (var row in query.Execute().AllResults())
        {
            if (Enum.TryParse<ScanType>(row.GetString("scanType"), out var type))
            {
                counts[type] = row.GetInt("total");
            }
        }

        return counts;
    }

    /// <summary>The query plan of the tracking-number query; shows it uses idx_scans_trackingNumber (screenshot evidence).</summary>
    public string ExplainTrackingNumberQuery()
    {
        using var query = db.CreateQuery(ByTrackingSql);
        query.Parameters = new Parameters().SetString("trackingNumber", "APX-00000000");
        return query.Explain();
    }

    /// <summary>The query plan of the pending-scans query; shows it uses idx_scans_syncStatus (screenshot evidence).</summary>
    public string ExplainPendingQuery()
    {
        using var query = db.CreateQuery(PendingSql);
        return query.Explain();
    }

    private IReadOnlyList<ShipmentScan> Load(IQuery query) =>
        query.Execute().AllResults()
            .Select(row => row.GetString("id"))
            .Select(id =>
            {
                if (id is null)
                {
                    return null;
                }

                using var doc = db.Scans.GetDocument(id);
                return doc is null ? null : Map(doc);
            })
            .OfType<ShipmentScan>()
            .ToList();

    private static void Write(MutableDocument doc, ShipmentScan scan) =>
        doc.SetString("type", "scan")
            .SetInt("schemaVersion", CourierDatabase.SchemaVersion)
            .SetString("trackingNumber", scan.TrackingNumber)
            .SetString("scanType", scan.ScanType.ToString())
            .SetString("courierId", scan.CourierId)
            .SetString("location", scan.Location)
            .SetString("scannedAtUtc", Iso.From(scan.ScannedAtUtc))
            .SetString("notes", scan.Notes)
            .SetString("syncStatus", scan.SyncStatus.ToString());

    private static ShipmentScan? Map(Document doc)
    {
        if (!ScanIds.TryParse(doc.Id, out var scanId))
        {
            return null;
        }

        return new ShipmentScan(
            scanId,
            doc.GetString("trackingNumber") ?? string.Empty,
            ParseEnum(doc.GetString("scanType"), ScanType.Pickup),
            doc.GetString("courierId") ?? string.Empty,
            doc.GetString("location") ?? string.Empty,
            Iso.Parse(doc.GetString("scannedAtUtc")) ?? DateTimeOffset.MinValue,
            doc.GetString("notes"),
            ParseEnum(doc.GetString("syncStatus"), SyncStatus.Pending));
    }

    private static T ParseEnum<T>(string? value, T fallback)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, out var parsed) ? parsed : fallback;
}

internal static class Iso
{
    public static string From(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    public static DateTimeOffset? Parse(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
}
