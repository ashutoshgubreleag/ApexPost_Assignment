using System.Collections.Concurrent;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Infrastructure.Couchbase;

namespace ApexPost.Courier.Offline.Tests;

/// <summary>The collection change listener: fires with document ids, keeps a live count, is removed on dispose.</summary>
public sealed class ChangeListenerTests : IDisposable
{
    private readonly CourierTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Saving_a_scan_raises_Changed_with_its_document_id_and_the_live_pending_count()
    {
        var logs = new ConcurrentQueue<string>();
        using var monitor = new PendingScanMonitor(_h.Database, _h.Scans, logs.Enqueue);
        var raised = new TaskCompletionSource<PendingScanChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += (_, e) => raised.TrySetResult(e);
        monitor.Start();

        var saved = _h.Scans.Create(CourierTestHarness.NewScan());

        var args = await raised.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(saved.DocumentId, args.DocumentIds);
        Assert.Equal(1, args.PendingCount);
        Assert.Equal(1, monitor.PendingCount);
        Assert.Contains(logs, line => line.Contains(saved.DocumentId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Marking_a_scan_Synced_lowers_the_live_pending_count()
    {
        var saved = _h.Scans.Create(CourierTestHarness.NewScan());
        using var monitor = new PendingScanMonitor(_h.Database, _h.Scans);
        Assert.Equal(0, monitor.PendingCount);
        monitor.Start();
        Assert.Equal(1, monitor.PendingCount);

        var raised = new TaskCompletionSource<PendingScanChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += (_, e) => raised.TrySetResult(e);
        _h.Scans.Update(saved with { SyncStatus = SyncStatus.Synced });

        var args = await raised.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, args.PendingCount);
        Assert.Equal(0, monitor.PendingCount);
    }

    [Fact]
    public void Starting_twice_registers_a_single_listener()
    {
        using var monitor = new PendingScanMonitor(_h.Database, _h.Scans);

        monitor.Start();
        monitor.Start();

        Assert.Equal(1, monitor.Registrations);
        Assert.True(monitor.IsListening);
    }

    [Fact]
    public async Task Disposing_the_monitor_removes_the_listener_token_so_no_more_events_arrive()
    {
        // Arrange
        var monitor = new PendingScanMonitor(_h.Database, _h.Scans);
        var events = 0;
        monitor.Changed += (_, _) => Interlocked.Increment(ref events);
        monitor.Start();

        // A second, live monitor is the positive control: once IT has seen the change, the database has delivered the
        // notification, so a listener that was still registered would have fired too. This replaces a fixed
        // Task.Delay(500) ("wait and hope") with waiting for a condition that is known to happen.
        using var control = new PendingScanMonitor(_h.Database, _h.Scans);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        control.Changed += (_, _) => delivered.TrySetResult();
        control.Start();

        // Act
        monitor.Dispose();
        monitor.Dispose();
        _h.Scans.Create(CourierTestHarness.NewScan());
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        Assert.False(monitor.IsListening);
        Assert.Equal(0, Volatile.Read(ref events));
    }

    [Fact]
    public void Disposing_the_database_disposes_the_monitor_first()
    {
        var monitor = new PendingScanMonitor(_h.Database, _h.Scans);
        monitor.Start();
        Assert.True(monitor.IsListening);

        _h.Database.Dispose();

        Assert.False(monitor.IsListening);
    }
}
