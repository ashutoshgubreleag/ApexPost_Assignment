using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Infrastructure.Couchbase;

namespace ApexPost.Courier.Offline.Tests;

/// <summary>The three SQL++ queries: parameters only, correct ordering and counts, indexes present.</summary>
public sealed class QueryTests : IDisposable
{
    private readonly CourierTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public void ByTrackingNumber_returns_only_that_shipment_ordered_by_scannedAtUtc()
    {
        // Saved out of order on purpose.
        _h.Scans.Create(CourierTestHarness.NewScan("APX-10000001", ScanType.Delivered, minutes: 300));
        _h.Scans.Create(CourierTestHarness.NewScan("APX-10000001", ScanType.Pickup, minutes: 0));
        _h.Scans.Create(CourierTestHarness.NewScan("APX-10000099", ScanType.Pickup, minutes: 10));
        _h.Scans.Create(CourierTestHarness.NewScan("APX-10000001", ScanType.InTransit, minutes: 120));

        var scans = _h.Scans.GetByTrackingNumber("APX-10000001");

        Assert.Equal([ScanType.Pickup, ScanType.InTransit, ScanType.Delivered], scans.Select(s => s.ScanType).ToArray());
        Assert.All(scans, s => Assert.Equal("APX-10000001", s.TrackingNumber));
    }

    [Fact]
    public void ByTrackingNumber_treats_hostile_text_as_a_value_not_as_query_text()
    {
        _h.Scans.Create(CourierTestHarness.NewScan("APX-10000001"));

        var scans = _h.Scans.GetByTrackingNumber("x' OR trackingNumber LIKE '%");

        Assert.Empty(scans);
    }

    [Fact]
    public void Pending_returns_only_pending_scans_oldest_first_and_honours_take()
    {
        var first = _h.Scans.Create(CourierTestHarness.NewScan("APX-10000001", minutes: 5));
        var second = _h.Scans.Create(CourierTestHarness.NewScan("APX-10000002", minutes: 1));
        var done = _h.Scans.Create(CourierTestHarness.NewScan("APX-10000003", minutes: 3));
        _h.Scans.Update(done with { SyncStatus = SyncStatus.Synced });

        var pending = _h.Scans.GetPending();
        var firstOnly = _h.Scans.GetPending(1);

        Assert.Equal([second.ScanId, first.ScanId], pending.Select(s => s.ScanId).ToArray());
        Assert.Equal([second.ScanId], firstOnly.Select(s => s.ScanId).ToArray());
        Assert.Equal(2, _h.Scans.CountPending());
    }

    [Fact]
    public void CountByScanType_counts_one_courier_and_zero_fills_missing_types()
    {
        _h.Scans.Create(CourierTestHarness.NewScan("APX-10000001", ScanType.Pickup, "C-100"));
        _h.Scans.Create(CourierTestHarness.NewScan("APX-10000002", ScanType.Pickup, "C-100"));
        _h.Scans.Create(CourierTestHarness.NewScan("APX-10000003", ScanType.Delivered, "C-100"));
        _h.Scans.Create(CourierTestHarness.NewScan("APX-10000004", ScanType.Failed, "C-200"));

        var counts = _h.Scans.CountByScanType("C-100");

        Assert.Equal(2, counts[ScanType.Pickup]);
        Assert.Equal(1, counts[ScanType.Delivered]);
        Assert.Equal(0, counts[ScanType.InTransit]);
        Assert.Equal(0, counts[ScanType.Failed]);
        Assert.Equal(4, counts.Count);
    }

    [Fact]
    public void Value_indexes_on_trackingNumber_and_syncStatus_are_used_by_the_queries()
    {
        var trackingPlan = _h.Scans.ExplainTrackingNumberQuery();
        var pendingPlan = _h.Scans.ExplainPendingQuery();

        Assert.Contains(CourierDatabase.TrackingIndexName, trackingPlan, StringComparison.Ordinal);
        Assert.Contains(CourierDatabase.SyncStatusIndexName, pendingPlan, StringComparison.Ordinal);
    }

    [Fact]
    public void Reopening_the_database_does_not_fail_when_the_indexes_already_exist()
    {
        _h.Reopen();
        _h.Reopen();

        Assert.Empty(_h.Scans.ListAll());
    }
}
