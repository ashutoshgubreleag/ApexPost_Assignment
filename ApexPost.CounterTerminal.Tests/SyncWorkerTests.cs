using ApexPost.Contracts;
using ApexPost.CounterTerminal.Domain;
using ApexPost.Models.ValueObjects;

namespace ApexPost.CounterTerminal.Tests;

/// <summary>Outbox push/pull against a fake server: failures never lose sales, retries never double-post.</summary>
public sealed class SyncWorkerTests : IDisposable
{
    private readonly TerminalHarness _h = new(online: false);

    public SyncWorkerTests() => _h.SeedCatalogue(stampQty: 10, boxQty: 3);

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Network_failure_keeps_sales_pending_and_marks_the_terminal_offline()
    {
        var sale = _h.Terminal.Sell([("STA-001", 1)], "Cash");

        var report = await _h.Worker.SyncOnceAsync(CancellationToken.None);

        Assert.False(report.Succeeded);
        var stored = _h.Sales.Get(_h.Options.DeviceId, sale.LocalTransactionId)!;
        Assert.Equal(SaleStatus.Pending, stored.Status);
        Assert.Equal(1, stored.Attempts);
        Assert.NotNull(stored.LastError);
        Assert.False(_h.Terminal.Status().IsOnline);
    }

    [Fact]
    public async Task Reconnecting_pushes_the_outbox_and_marks_sales_synced()
    {
        var first = _h.Terminal.Sell([("STA-001", 2)], "Cash");
        var second = _h.Terminal.Sell([("PKG-002", 1)], "Card");
        await _h.Worker.SyncOnceAsync(CancellationToken.None);

        _h.Network.IsDisconnected = false;
        _h.Api.Snapshot = new BranchSnapshotResponse(1, "BR-001", TerminalHarness.Start.AddMinutes(5), true, [], [new StockSnapshotDto("STA-001", 8, 2)]);
        var report = await _h.Worker.SyncOnceAsync(CancellationToken.None);

        Assert.True(report.Succeeded);
        Assert.Equal(2, report.Accepted);
        Assert.Equal(SaleStatus.Synced, _h.Sales.Get(_h.Options.DeviceId, first.LocalTransactionId)!.Status);
        Assert.NotNull(_h.Sales.Get(_h.Options.DeviceId, second.LocalTransactionId)!.ServerTransactionId);
        Assert.Equal(0, _h.Terminal.Status().PendingCount);
        Assert.True(_h.Terminal.Status().IsOnline);

        // Server now counts the 2 stamps (8 left); the device-owned counter is back to zero.
        var stamps = _h.Stock.Get(_h.Branch, new Sku("STA-001"))!;
        Assert.Equal(8, stamps.ServerQuantity);
        Assert.Equal(0, stamps.LocalSoldSinceSync);
    }

    [Fact]
    public async Task Lost_response_then_retry_does_not_create_a_second_server_sale()
    {
        var sale = _h.Terminal.Sell([("STA-001", 1)], "Cash");
        _h.Network.IsDisconnected = false;
        _h.Api.LoseNextResponse = true;

        var failed = await _h.Worker.SyncOnceAsync(CancellationToken.None);
        Assert.False(failed.Succeeded);
        Assert.Equal(SaleStatus.Pending, _h.Sales.Get(_h.Options.DeviceId, sale.LocalTransactionId)!.Status);

        var retried = await _h.Worker.SyncOnceAsync(CancellationToken.None);

        Assert.True(retried.Succeeded);
        Assert.Equal(1, _h.Api.ServerSaleCount);
        Assert.Equal(2, _h.Api.Pushes.Count);
        Assert.Equal(SaleStatus.Synced, _h.Sales.Get(_h.Options.DeviceId, sale.LocalTransactionId)!.Status);
    }

    [Fact]
    public async Task Accepted_with_warnings_is_synced_and_keeps_the_warning()
    {
        var sale = _h.Terminal.Sell([("PKG-002", 3)], "Cash");
        _h.Network.IsDisconnected = false;
        _h.Api.Decide = _ => ("AcceptedWithWarnings", ["StockShortfall: PKG-002 short by 1"]);

        var report = await _h.Worker.SyncOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.Warnings);
        var stored = _h.Sales.Get(_h.Options.DeviceId, sale.LocalTransactionId)!;
        Assert.Equal(SaleStatus.Synced, stored.Status);
        Assert.Contains(stored.Warnings, w => w.StartsWith("StockShortfall", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rejected_sale_is_not_retried_and_its_units_are_released()
    {
        var sale = _h.Terminal.Sell([("STA-001", 4)], "Cash");
        _h.Network.IsDisconnected = false;
        _h.Api.Decide = _ => ("Rejected", []);

        await _h.Worker.SyncOnceAsync(CancellationToken.None);
        await _h.Worker.SyncOnceAsync(CancellationToken.None);

        Assert.Equal(SaleStatus.Rejected, _h.Sales.Get(_h.Options.DeviceId, sale.LocalTransactionId)!.Status);
        Assert.Single(_h.Api.Pushes);
        Assert.Equal(0, _h.Stock.Get(_h.Branch, new Sku("STA-001"))!.LocalSoldSinceSync);
    }

    [Fact]
    public async Task Pull_updates_prices_in_the_local_catalogue()
    {
        _h.Network.IsDisconnected = false;
        _h.Api.Snapshot = new BranchSnapshotResponse(1, "BR-001", TerminalHarness.Start.AddMinutes(1), true,
            [new ProductSnapshotDto("STA-001", "Stamp book", "Stationery", 13.75m, "Standard", true)], []);

        await _h.Worker.SyncOnceAsync(CancellationToken.None);

        Assert.Equal(new Money(13.75m), _h.Catalog.Get(new Sku("STA-001"))!.UnitPrice);
        Assert.Equal(TerminalHarness.Start.AddMinutes(1), _h.SyncState.Get().LastSnapshotAt);
    }

    [Fact]
    public async Task Backoff_grows_after_failures_is_capped_and_resets_after_success()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), _h.Worker.NextDelay());

        await _h.Worker.SyncOnceAsync(CancellationToken.None);
        var afterOne = _h.Worker.NextDelay();
        for (var i = 0; i < 8; i++)
        {
            await _h.Worker.SyncOnceAsync(CancellationToken.None);
        }

        var afterMany = _h.Worker.NextDelay();
        Assert.InRange(afterOne.TotalSeconds, 2, 2.5);
        Assert.InRange(afterMany.TotalSeconds, 60, 75);

        _h.Network.IsDisconnected = false;
        await _h.Worker.SyncOnceAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(30), _h.Worker.NextDelay());
    }

    [Fact]
    public async Task Concurrent_sync_requests_run_one_at_a_time()
    {
        _h.Terminal.Sell([("STA-001", 1)], "Cash");
        _h.Network.IsDisconnected = false;

        await Task.WhenAll(_h.Worker.SyncOnceAsync(CancellationToken.None), _h.Worker.SyncOnceAsync(CancellationToken.None));

        Assert.Equal(1, _h.Api.Pushes.Sum(p => p.Sales.Count));
    }
}
