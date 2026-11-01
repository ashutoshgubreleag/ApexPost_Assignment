using ApexPost.CounterTerminal.Infrastructure.Couchbase;

namespace ApexPost.CounterTerminal.Tests;

/// <summary>The pendingTransactions change listener: registered once, IDs only, removed on dispose.</summary>
public sealed class ChangeMonitorTests : IDisposable
{
    private readonly TerminalHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Listener_reports_the_saved_sale_document_once()
    {
        _h.SeedCatalogue();
        using var monitor = new TerminalChangeMonitor(_h.Database);
        monitor.Start();

        var sale = _h.Terminal.Sell([("STA-001", 1)], "Cash");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ids = await monitor.WaitForChangesAsync(timeout.Token);
        Assert.Equal([sale.DocumentId], ids);
    }

    [Fact]
    public async Task Starting_twice_registers_a_single_listener()
    {
        _h.SeedCatalogue();
        using var monitor = new TerminalChangeMonitor(_h.Database);
        monitor.Start();
        monitor.Start();

        var sale = _h.Terminal.Sell([("STA-001", 1)], "Cash");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ids = await monitor.WaitForChangesAsync(timeout.Token);

        // Give a second (duplicate) listener time to fire, then make sure nothing else arrived.
        await Task.Delay(300);
        using var none = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await monitor.WaitForChangesAsync(none.Token));

        Assert.Equal(1, monitor.Registrations);
        Assert.Equal([sale.DocumentId], ids);
    }

    [Fact]
    public void Disposing_the_monitor_before_the_database_closes_cleanly()
    {
        var monitor = new TerminalChangeMonitor(_h.Database);
        monitor.Start();

        monitor.Dispose();
        monitor.Dispose();
        _h.Reopen();

        Assert.NotNull(_h.Database.Pending);
    }
}
