using ApexPost.Courier.Offline.Application;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Tests;

/// <summary>The outbox: local-first Pending scans become Synced or Failed; unreachable servers lose nothing.</summary>
public sealed class OutboxTests : IDisposable
{
    private readonly CourierTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Pending_scans_become_Synced_or_Failed_according_to_the_server_answer()
    {
        var ok1 = _h.Scans.Create(CourierTestHarness.NewScan("APX-10000001", minutes: 1));
        var ok2 = _h.Scans.Create(CourierTestHarness.NewScan("APX-10000002", minutes: 2));
        var bad = _h.Scans.Create(CourierTestHarness.NewScan("APX-10000013", minutes: 3));
        var outbox = new OutboxService(_h.Scans, new FakeShipmentUploader("13"));

        var result = await outbox.SyncPendingAsync();

        Assert.Equal(new OutboxSyncResult(3, 2, 1, false, null), result);
        Assert.Equal(SyncStatus.Synced, _h.Scans.Get(ok1.ScanId)!.SyncStatus);
        Assert.Equal(SyncStatus.Synced, _h.Scans.Get(ok2.ScanId)!.SyncStatus);
        Assert.Equal(SyncStatus.Failed, _h.Scans.Get(bad.ScanId)!.SyncStatus);
        Assert.Equal(0, _h.Scans.CountPending());
    }

    [Fact]
    public async Task A_new_scan_is_saved_locally_as_Pending_before_any_upload_happens()
    {
        var uploader = new FakeShipmentUploader { Offline = true };
        var saved = _h.Scans.Create(CourierTestHarness.NewScan());

        var result = await new OutboxService(_h.Scans, uploader).SyncPendingAsync();

        Assert.True(result.Interrupted);
        Assert.Equal(SyncStatus.Pending, _h.Scans.Get(saved.ScanId)!.SyncStatus);
    }

    [Fact]
    public async Task An_unreachable_server_leaves_every_scan_Pending_and_reports_why()
    {
        _h.Scans.Create(CourierTestHarness.NewScan("APX-10000001"));
        _h.Scans.Create(CourierTestHarness.NewScan("APX-10000002"));
        var outbox = new OutboxService(_h.Scans, new FakeShipmentUploader { Offline = true });

        var result = await outbox.SyncPendingAsync();

        Assert.True(result.Interrupted);
        Assert.Equal(0, result.Synced);
        Assert.Equal(0, result.Failed);
        Assert.Contains("unreachable", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, _h.Scans.CountPending());
    }

    [Fact]
    public async Task Scans_are_uploaded_in_batches_of_the_configured_size()
    {
        for (var i = 0; i < 5; i++)
        {
            _h.Scans.Create(CourierTestHarness.NewScan($"APX-1000000{i}", minutes: i));
        }

        var recorder = new RecordingUploader();
        var result = await new OutboxService(_h.Scans, recorder, batchSize: 2).SyncPendingAsync();

        Assert.Equal([2, 2, 1], recorder.BatchSizes.ToArray());
        Assert.Equal(5, result.Synced);
    }

    [Fact]
    public void The_default_batch_size_is_20_and_a_zero_batch_size_is_rejected()
    {
        Assert.Equal(20, new OutboxService(_h.Scans, new FakeShipmentUploader()).BatchSize);
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboxService(_h.Scans, new FakeShipmentUploader(), 0));
    }

    [Fact]
    public async Task Cancellation_is_not_a_failure_it_propagates_and_scans_stay_Pending()
    {
        var saved = _h.Scans.Create(CourierTestHarness.NewScan());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new OutboxService(_h.Scans, new FakeShipmentUploader()).SyncPendingAsync(cts.Token));

        Assert.Equal(SyncStatus.Pending, _h.Scans.Get(saved.ScanId)!.SyncStatus);
    }

    [Fact]
    public async Task An_uploader_that_answers_for_no_scan_cannot_make_the_outbox_loop_forever()
    {
        _h.Scans.Create(CourierTestHarness.NewScan());

        var result = await new OutboxService(_h.Scans, new SilentUploader()).SyncPendingAsync();

        Assert.True(result.Interrupted);
        Assert.Equal(1, _h.Scans.CountPending());
    }

    private sealed class RecordingUploader : IShipmentUploader
    {
        public List<int> BatchSizes { get; } = [];

        public Task<IReadOnlyList<ScanUploadOutcome>> UploadAsync(IReadOnlyList<ShipmentScan> batch, CancellationToken cancellationToken)
        {
            BatchSizes.Add(batch.Count);
            IReadOnlyList<ScanUploadOutcome> outcomes = batch.Select(s => new ScanUploadOutcome(s.ScanId, true)).ToList();
            return Task.FromResult(outcomes);
        }
    }

    private sealed class SilentUploader : IShipmentUploader
    {
        public Task<IReadOnlyList<ScanUploadOutcome>> UploadAsync(IReadOnlyList<ShipmentScan> batch, CancellationToken cancellationToken)
        {
            IReadOnlyList<ScanUploadOutcome> none = [];
            return Task.FromResult(none);
        }
    }
}
