using ApexPost.Contracts;
using ApexPost.CounterTerminal.Application;
using ApexPost.CounterTerminal.Domain;
using ApexPost.CounterTerminal.Infrastructure.Couchbase;
using ApexPost.Models.ValueObjects;

namespace ApexPost.CounterTerminal.Tests;

/// <summary>A real Couchbase Lite database in a throw-away directory, with the stores wired up.</summary>
public sealed class TerminalHarness : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    private TerminalDatabase? _database;

    public TerminalHarness(bool online = false)
    {
        Directory = Path.Combine(Path.GetTempPath(), "apexpost-terminal-tests", Guid.NewGuid().ToString("N"));
        Options = new TerminalOptions { DeviceId = "TERM-T-01", BranchCode = "BR-001", DataDirectory = Directory, MoneyOrderIdentityThreshold = 1000m, SyncIntervalSeconds = 30, MaxBackoffSeconds = 60 };
        Clock = new ManualClock(Start);
        Network = new NetworkSwitch(!online);
        Api = new FakeSyncApi(Network);
        Open();
        SyncState.Save(SyncState.Get() with { IsOnline = online });
    }

    public string Directory { get; }

    public TerminalOptions Options { get; }

    public ManualClock Clock { get; }

    public NetworkSwitch Network { get; }

    public FakeSyncApi Api { get; }

    public TerminalDatabase Database => _database!;

    public CouchbaseCatalogStore Catalog { get; private set; } = null!;

    public CouchbaseStockStore Stock { get; private set; } = null!;

    public CouchbasePendingSaleStore Sales { get; private set; } = null!;

    public CouchbaseSyncStateStore SyncState { get; private set; } = null!;

    public TerminalService Terminal { get; private set; } = null!;

    public SyncWorker Worker { get; private set; } = null!;

    public BranchCode Branch => new(Options.BranchCode);

    /// <summary>Closes and reopens the database from disk (simulates an app restart).</summary>
    public void Reopen()
    {
        _database?.Dispose();
        Open();
    }

    /// <summary>Two products, stock for both, as if a first sync had happened.</summary>
    public void SeedCatalogue(int stampQty = 10, int boxQty = 3)
    {
        Catalog.Upsert(
        [
            new CatalogItem(new Sku("STA-001"), "Stamp book", "Stationery", new Money(12.50m), "Standard", true),
            new CatalogItem(new Sku("PKG-002"), "Medium box", "Packaging", new Money(3.20m), "Standard", true),
            new CatalogItem(new Sku("STA-002"), "Envelope pack", "Stationery", new Money(2.10m), "Standard", true),
            new CatalogItem(new Sku("STA-009"), "Discontinued pad", "Stationery", new Money(1.00m), "Standard", false),
        ]);
        Stock.ApplyServerSnapshot(Branch, [new StockSnapshotDto("STA-001", stampQty, 2), new StockSnapshotDto("PKG-002", boxQty, 1), new StockSnapshotDto("STA-002", 50, 5)], Start);
    }

    public void Dispose()
    {
        _database?.Dispose();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the OS may still hold a file handle for a moment.
        }
    }

    private void Open()
    {
        _database = new TerminalDatabase(Directory);
        Catalog = new CouchbaseCatalogStore(_database);
        Stock = new CouchbaseStockStore(_database);
        Sales = new CouchbasePendingSaleStore(_database);
        SyncState = new CouchbaseSyncStateStore(_database, Options);
        Terminal = new TerminalService(Catalog, Stock, Sales, SyncState, Options, Clock);
        Worker = new SyncWorker(Sales, Catalog, Stock, SyncState, Api, Options, Clock);
    }
}

public sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// Stands in for the server's /api/sync endpoints. Remembers what it has received by
/// LocalTransactionId (like the server's SyncInbox) so a re-push reports Duplicate, not a second sale.
/// </summary>
public sealed class FakeSyncApi(INetworkSwitch network) : ISyncApiClient
{
    private readonly Dictionary<Guid, OfflineSaleResultDto> _received = [];
    private int _nextId = 1000;

    public List<PushTransactionsRequest> Pushes { get; } = [];

    public int PullCount { get; private set; }

    /// <summary>Decides the outcome of a newly received sale (default: accepted).</summary>
    public Func<OfflineSaleDto, (string Outcome, IReadOnlyList<string> Warnings)> Decide { get; set; } = _ => ("Accepted", []);

    /// <summary>When true the push is applied on the "server" but the response is lost (timeout).</summary>
    public bool LoseNextResponse { get; set; }

    public int ServerSaleCount => _received.Values.Count(r => r.ServerTransactionId is not null);

    public BranchSnapshotResponse Snapshot { get; set; } = new(1, "BR-001", TerminalHarness.Start, false, [], []);

    public Task<PushTransactionsResponse> PushAsync(PushTransactionsRequest request, CancellationToken cancellationToken)
    {
        if (network.IsDisconnected)
        {
            throw new SyncUnavailableException("offline");
        }

        Pushes.Add(request);
        var results = new List<OfflineSaleResultDto>();
        foreach (var sale in request.Sales)
        {
            if (_received.TryGetValue(sale.LocalTransactionId, out var earlier))
            {
                results.Add(earlier with { Duplicate = true });
                continue;
            }

            var (outcome, warnings) = Decide(sale);
            var result = new OfflineSaleResultDto(sale.LocalTransactionId, outcome, outcome == "Rejected" ? null : _nextId++, outcome, warnings, false);
            _received[sale.LocalTransactionId] = result;
            results.Add(result);
        }

        if (LoseNextResponse)
        {
            LoseNextResponse = false;
            throw new SyncUnavailableException("Server did not answer in time.");
        }

        return Task.FromResult(new PushTransactionsResponse(results));
    }

    public Task<BranchSnapshotResponse> PullAsync(DateTimeOffset? since, CancellationToken cancellationToken)
    {
        if (network.IsDisconnected)
        {
            throw new SyncUnavailableException("offline");
        }

        PullCount++;
        return Task.FromResult(Snapshot);
    }
}
