using System.Text.RegularExpressions;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Infrastructure.Couchbase;

namespace ApexPost.Courier.Offline.Tests;

/// <summary>Key-value CRUD, document model and database lifecycle.</summary>
public sealed class RepositoryTests : IDisposable
{
    private readonly CourierTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public void Create_uses_the_scan_guid_document_id_format()
    {
        var saved = _h.Scans.Create(CourierTestHarness.NewScan());

        Assert.Matches(new Regex("^scan::[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"), saved.DocumentId);
        Assert.Equal($"scan::{saved.ScanId:D}", saved.DocumentId);
        Assert.True(ScanIds.TryParse(saved.DocumentId, out var parsed));
        Assert.Equal(saved.ScanId, parsed);
        Assert.False(ScanIds.TryParse("order::123", out _));
    }

    [Fact]
    public void Create_always_stores_the_scan_as_Pending()
    {
        var wantsSynced = CourierTestHarness.NewScan() with { SyncStatus = SyncStatus.Synced };

        var saved = _h.Scans.Create(wantsSynced);

        Assert.Equal(SyncStatus.Pending, saved.SyncStatus);
        Assert.Equal(SyncStatus.Pending, _h.Scans.Get(saved.ScanId)!.SyncStatus);
    }

    [Fact]
    public void Create_twice_with_the_same_id_is_rejected()
    {
        var saved = _h.Scans.Create(CourierTestHarness.NewScan());

        Assert.Throws<InvalidOperationException>(() => _h.Scans.Create(saved));
    }

    [Fact]
    public void Get_is_a_key_value_lookup_that_round_trips_every_field()
    {
        var original = CourierTestHarness.NewScan("APX-10000007", ScanType.Delivered, "C-777", 15, "12 Fictional Lane") with { Notes = "Left with neighbour" };
        var saved = _h.Scans.Create(original);

        var loaded = _h.Scans.Get(saved.ScanId);

        Assert.NotNull(loaded);
        Assert.Equal(saved with { SyncStatus = SyncStatus.Pending }, loaded);
        Assert.Equal("Left with neighbour", loaded.Notes);
        Assert.Equal(ScanType.Delivered, loaded.ScanType);
    }

    [Fact]
    public void Get_of_an_unknown_id_returns_null()
    {
        Assert.Null(_h.Scans.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Update_changes_the_stored_fields_and_survives_a_restart()
    {
        var saved = _h.Scans.Create(CourierTestHarness.NewScan());

        var changed = saved with { Location = "Sorting Hub North", Notes = "Re-scanned", ScanType = ScanType.InTransit };
        Assert.True(_h.Scans.Update(changed));
        _h.Reopen();

        var loaded = _h.Scans.Get(saved.ScanId);
        Assert.Equal("Sorting Hub North", loaded!.Location);
        Assert.Equal("Re-scanned", loaded.Notes);
        Assert.Equal(ScanType.InTransit, loaded.ScanType);
    }

    [Fact]
    public void Update_of_a_missing_scan_returns_false()
    {
        Assert.False(_h.Scans.Update(CourierTestHarness.NewScan()));
    }

    [Fact]
    public void Delete_removes_the_scan_and_reports_missing_ones()
    {
        var saved = _h.Scans.Create(CourierTestHarness.NewScan());

        Assert.True(_h.Scans.Delete(saved.ScanId));
        Assert.Null(_h.Scans.Get(saved.ScanId));
        Assert.False(_h.Scans.Delete(saved.ScanId));
    }

    [Fact]
    public void ListAll_returns_every_scan()
    {
        _h.Scans.Create(CourierTestHarness.NewScan("APX-10000001", minutes: 2));
        _h.Scans.Create(CourierTestHarness.NewScan("APX-10000002", minutes: 1));
        _h.Scans.Create(CourierTestHarness.NewScan("APX-10000003", minutes: 3));

        var all = _h.Scans.ListAll();

        Assert.Equal(3, all.Count);
        Assert.Equal(["APX-10000002", "APX-10000001", "APX-10000003"], all.Select(s => s.TrackingNumber).ToArray());
    }

    [Fact]
    public void The_database_is_named_apexpost_courier_and_uses_the_default_collection()
    {
        Assert.Equal("apexpost_courier", CourierDatabase.DatabaseName);
        Assert.Contains("apexpost_courier", _h.Database.DatabasePath, StringComparison.Ordinal);
        Assert.Equal("_default", _h.Database.Scans.Name);
    }

    [Fact]
    public void Reopening_the_database_keeps_the_data_and_disposing_twice_is_safe()
    {
        var saved = _h.Scans.Create(CourierTestHarness.NewScan());

        _h.Reopen();
        _h.Database.Dispose();

        Assert.NotNull(saved);
        _h.Reopen();
        Assert.NotNull(_h.Scans.Get(saved.ScanId));
    }
}
