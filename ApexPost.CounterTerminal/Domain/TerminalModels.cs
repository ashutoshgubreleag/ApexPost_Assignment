using ApexPost.Models.ValueObjects;

namespace ApexPost.CounterTerminal.Domain;

/// <summary>A product in the terminal's local catalogue snapshot (server-owned: the terminal never edits it).</summary>
public sealed record CatalogItem(Sku Sku, string Name, string Category, Money UnitPrice, string TaxCategory, bool IsActive);

/// <summary>
/// Local stock for one SKU. Field ownership (CONFLICTS.md C3):
///   server-owned: ServerQuantity, ReorderThreshold, ServerSyncedAt (always replaced by a pull);
///   device-owned: LocalSoldSinceSync, LastLocalSaleAt (never sent to, or overwritten by, the server).
/// </summary>
public sealed record StockLevel(
    BranchCode BranchCode,
    Sku Sku,
    int ServerQuantity,
    int ReorderThreshold,
    DateTimeOffset? ServerSyncedAt,
    int LocalSoldSinceSync,
    DateTimeOffset? LastLocalSaleAt)
{
    /// <summary>What can still be sold offline without going below zero.</summary>
    public int Available => Math.Max(0, ServerQuantity - LocalSoldSinceSync);
}

public enum SaleStatus
{
    /// <summary>Saved locally, waiting to be pushed (the outbox).</summary>
    Pending,

    /// <summary>The server recorded it (possibly with warnings).</summary>
    Synced,

    /// <summary>The server refused it (for example an unknown clerk); needs a supervisor.</summary>
    Rejected,

    /// <summary>The request was malformed; kept for investigation, not retried.</summary>
    Invalid,
}

public sealed record SaleLine(Sku Sku, string Name, int Quantity, Money UnitPrice)
{
    public Money LineTotal => (UnitPrice * Quantity).RoundedToCents;
}

public sealed record PendingMoneyOrder(Money Amount, string Sender, string Recipient);

/// <summary>A sale captured at this terminal. Immutable after capture except for its sync metadata.</summary>
public sealed record PendingSale(
    Guid LocalTransactionId,
    string DeviceId,
    BranchCode BranchCode,
    int CounterNumber,
    int ClerkStaffId,
    DateTimeOffset CapturedAt,
    string PaymentMethod,
    IReadOnlyList<SaleLine> Lines,
    IReadOnlyList<PendingMoneyOrder> MoneyOrders,
    SaleStatus Status,
    int Attempts,
    DateTimeOffset? LastAttemptAt,
    string? LastError,
    int? ServerTransactionId,
    IReadOnlyList<string> Warnings)
{
    public string DocumentId => TerminalIds.Transaction(DeviceId, LocalTransactionId);

    /// <summary>Goods + money order face values (tax is computed by the server on sync).</summary>
    public Money Total => Lines.Select(l => l.LineTotal).Total() + MoneyOrders.Select(m => m.Amount).Total();
}

/// <summary>Sync status shown to the clerk.</summary>
public sealed record SyncState(
    string DeviceId,
    BranchCode BranchCode,
    bool IsOnline,
    DateTimeOffset? LastSuccessfulSyncAt,
    DateTimeOffset? LastSnapshotAt,
    string? LastError,
    DateTimeOffset? LastErrorAt);

/// <summary>Stable document IDs that are unique without talking to the server.</summary>
public static class TerminalIds
{
    public static string Product(Sku sku) => $"product::{sku.Value}";

    public static string Stock(BranchCode branch, Sku sku) => $"stock::{branch.Value}::{sku.Value}";

    public static string Transaction(string deviceId, Guid localId) => $"tx::{deviceId}::{localId:D}";

    public static string SyncState(string deviceId) => $"syncstate::{deviceId}";
}
