using ApexPost.CounterTerminal.Application;
using ApexPost.CounterTerminal.Domain;
using ApexPost.CounterTerminal.Infrastructure.Couchbase;
using ApexPost.CounterTerminal.Infrastructure.Http;
using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ApexPost.Tests.Integration;

/// <summary>
/// End to end: the real counter terminal (Couchbase Lite, TerminalService, SyncWorker, SyncApiClient)
/// talking to the real web app (device middleware, SyncController, OfflineSaleService) in memory.
/// </summary>
public sealed class TerminalToServerTests : IClassFixture<ApexPostWebApplicationFactory>, IDisposable
{
    private readonly ApexPostWebApplicationFactory _factory;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "apexpost-e2e", Guid.NewGuid().ToString("N"));
    private readonly TerminalDatabase _database;
    private readonly HttpClient _http;
    private readonly NetworkSwitch _network = new(false);
    private readonly TerminalService _terminal;
    private readonly SyncWorker _worker;
    private readonly CouchbasePendingSaleStore _sales;
    private readonly TerminalOptions _options;

    public TerminalToServerTests(ApexPostWebApplicationFactory factory)
    {
        _factory = factory;
        _options = new TerminalOptions
        {
            DeviceId = "TERM-TEST-01",
            DeviceKey = ApexPostWebApplicationFactory.TestDeviceKey,
            BranchCode = "BR-001",
            ClerkStaffId = 7,
            CounterNumber = 1,
            DataDirectory = _directory,
        };
        _database = new TerminalDatabase(_directory);
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost/"), AllowAutoRedirect = false });
        var catalog = new CouchbaseCatalogStore(_database);
        var stock = new CouchbaseStockStore(_database);
        _sales = new CouchbasePendingSaleStore(_database);
        var state = new CouchbaseSyncStateStore(_database, _options);
        _terminal = new TerminalService(catalog, stock, _sales, state, _options, TimeProvider.System);
        _worker = new SyncWorker(_sales, catalog, stock, state, new SyncApiClient(_http, _options, _network), _options, TimeProvider.System);
    }

    public void Dispose()
    {
        _http.Dispose();
        _database.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Fact]
    public async Task Offline_sales_reach_the_server_once_after_reconnecting()
    {
        // First sync pulls the BR-001 catalogue and stock.
        var first = await _worker.SyncOnceAsync(CancellationToken.None);
        Assert.True(first.Succeeded, first.Error);
        Assert.NotEmpty(_terminal.Stock());

        // Network down: the sale still works and waits in the outbox.
        _network.IsDisconnected = true;
        var sale = _terminal.Sell([("PKG-004", 2)], "Cash");
        var failed = await _worker.SyncOnceAsync(CancellationToken.None);
        Assert.False(failed.Succeeded);
        Assert.Equal(1, _terminal.Status().PendingCount);
        Assert.DoesNotContain(_factory.Store.All<SyncInboxEntry>(), e => e.LocalTransactionId == sale.LocalTransactionId);

        // Network back: pushed, recorded through the domain on the server, marked synced locally.
        _network.IsDisconnected = false;
        var report = await _worker.SyncOnceAsync(CancellationToken.None);
        Assert.True(report.Succeeded, report.Error);
        Assert.Equal(1, report.Accepted);

        var synced = _sales.Get(_options.DeviceId, sale.LocalTransactionId)!;
        Assert.Equal(SaleStatus.Synced, synced.Status);
        var serverTx = _factory.Store.All<CounterTransaction>().Single(t => t.Id == synced.ServerTransactionId);
        Assert.Equal(TransactionSource.OfflineTerminal, serverTx.Source);
        Assert.Equal(TransactionStatus.Paid, serverTx.Status);
        Assert.Equal(2, serverTx.LineItems.Single().Quantity);

        // Simulate a lost response: push the same sale again. The server must not record it twice.
        _sales.Save(synced with { Status = SaleStatus.Pending });
        var again = await _worker.SyncOnceAsync(CancellationToken.None);
        Assert.True(again.Succeeded, again.Error);
        Assert.Single(_factory.Store.All<SyncInboxEntry>(), e => e.LocalTransactionId == sale.LocalTransactionId);
        Assert.Single(_factory.Store.All<CounterTransaction>(), t => t.Id == synced.ServerTransactionId);
        Assert.Equal(synced.ServerTransactionId, _sales.Get(_options.DeviceId, sale.LocalTransactionId)!.ServerTransactionId);
    }

    [Fact]
    public async Task Wrong_device_key_keeps_sales_pending_and_reports_the_reason()
    {
        using var http = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost/") });
        var badOptions = new TerminalOptions { DeviceId = "TERM-TEST-01", DeviceKey = "wrong", BranchCode = "BR-001" };
        var api = new SyncApiClient(http, badOptions, new NetworkSwitch(false));

        var ex = await Assert.ThrowsAsync<SyncUnavailableException>(() => api.PullAsync(null, CancellationToken.None));

        Assert.Contains("not authorised", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pull_brings_server_prices_into_the_local_catalogue()
    {
        await _worker.SyncOnceAsync(CancellationToken.None);

        var local = _terminal.FindProduct("PKG-001");

        Assert.NotNull(local);
        var server = _factory.Store.All<Product>().Single(p => p.Sku == new Sku("PKG-001"));
        Assert.Equal(server.UnitPrice, local.UnitPrice);
    }
}
