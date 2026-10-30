using ApexPost.Contracts;
using ApexPost.CounterTerminal.Domain;
using ApexPost.Models.ValueObjects;

namespace ApexPost.CounterTerminal.Application;

/*
 * Ports used by the terminal's application layer. The Couchbase Lite implementations live in
 * Infrastructure/Couchbase; nothing here (or in the UI) knows about Couchbase types.
 */

public interface ICatalogStore
{
    /// <summary>Key-value lookup: product::{Sku}.</summary>
    CatalogItem? Get(Sku sku);

    /// <summary>SQL++: active products in a category, sorted by name.</summary>
    IReadOnlyList<CatalogItem> ListByCategory(string category);

    IReadOnlyList<string> Categories();

    /// <summary>Server-owned documents: the pulled values simply replace the local ones.</summary>
    void Upsert(IEnumerable<CatalogItem> items);
}

public interface IStockStore
{
    StockLevel? Get(BranchCode branch, Sku sku);

    IReadOnlyList<StockLevel> List(BranchCode branch);

    void Save(StockLevel stock);

    /// <summary>Applies a pulled server snapshot with the field-level merge policy.</summary>
    void ApplyServerSnapshot(BranchCode branch, IEnumerable<StockSnapshotDto> rows, DateTimeOffset syncedAt);
}

public interface IPendingSaleStore
{
    /// <summary>Key-value lookup: tx::{deviceId}::{guid}.</summary>
    PendingSale? Get(string deviceId, Guid localTransactionId);

    void Save(PendingSale sale);

    IReadOnlyList<PendingSale> ListByStatus(SaleStatus status, int take);

    /// <summary>SQL++: pending sales captured more than <paramref name="age"/> ago (stuck in the outbox).</summary>
    IReadOnlyList<PendingSale> ListPendingOlderThan(TimeSpan age, DateTimeOffset now);

    /// <summary>SQL++: number and value of today's local sales (any status except rejected).</summary>
    (int Count, Money Total) TodaysTotals(DateOnly today);

    int CountPending();
}

public interface ISyncStateStore
{
    SyncState Get();

    void Save(SyncState state);
}

/// <summary>The sync endpoints of ApexPost.Web. Throws <see cref="SyncUnavailableException"/> when the network or server is down.</summary>
public interface ISyncApiClient
{
    Task<PushTransactionsResponse> PushAsync(PushTransactionsRequest request, CancellationToken cancellationToken);

    Task<BranchSnapshotResponse> PullAsync(DateTimeOffset? since, CancellationToken cancellationToken);
}

/// <summary>Raised by the change monitor when sales are written locally (IDs only).</summary>
public interface IPendingSaleChangeFeed
{
    /// <summary>Completes when at least one change is waiting; returns the document IDs seen.</summary>
    ValueTask<IReadOnlyList<string>> WaitForChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Simulated network cable for demos and tests ("offline" / "online" commands).</summary>
public interface INetworkSwitch
{
    bool IsDisconnected { get; set; }
}

public sealed class NetworkSwitch(bool disconnected) : INetworkSwitch
{
    public bool IsDisconnected { get; set; } = disconnected;
}

/// <summary>The server cannot be reached (network down, server down, timeout). Not a data error: retry later.</summary>
public sealed class SyncUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>Terminal settings (section <c>Terminal</c>).</summary>
public sealed class TerminalOptions
{
    public const string SectionName = "Terminal";

    public string DeviceId { get; set; } = "TERM-BR001-01";

    public string BranchCode { get; set; } = "BR-001";

    public int CounterNumber { get; set; } = 1;

    public int ClerkStaffId { get; set; } = 7;

    /// <summary>Persistent, configurable directory for the Couchbase Lite database.</summary>
    public string DataDirectory { get; set; } = "terminal-data";

    public string ServerBaseUrl { get; set; } = "https://localhost:7243";

    /// <summary>From user-secrets (Terminal:DeviceKey), never committed.</summary>
    public string DeviceKey { get; set; } = string.Empty;

    public bool ForceOffline { get; set; }

    public int SyncIntervalSeconds { get; set; } = 30;

    public decimal MoneyOrderIdentityThreshold { get; set; } = 1000m;

    public int MaxBackoffSeconds { get; set; } = 60;
}
