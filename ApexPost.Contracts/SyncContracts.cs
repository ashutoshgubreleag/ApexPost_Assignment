namespace ApexPost.Contracts;

/// <summary>Headers used by counter terminals on /api/sync (HTTPS only).</summary>
public static class SyncHeaders
{
    public const string DeviceId = "X-Device-Id";
    public const string DeviceKey = "X-Device-Key";
}

/// <summary>POST /api/sync/transactions body: a batch of offline sales from one device.</summary>
public sealed record PushTransactionsRequest(int SchemaVersion, IReadOnlyList<OfflineSaleDto> Sales);

/// <summary>
/// One offline sale. Contains only the facts captured at the counter: what was sold, at what
/// price, when and how it was paid. Server-owned values (stock, current prices, ids, status) are
/// never sent; the server derives them (CONFLICTS.md, C3).
/// </summary>
public sealed record OfflineSaleDto(
    Guid LocalTransactionId,
    int CounterNumber,
    int ClerkStaffId,
    DateTimeOffset CapturedAtUtc,
    string PaymentMethod,
    IReadOnlyList<OfflineSaleLineDto> Lines,
    IReadOnlyList<OfflineMoneyOrderDto> MoneyOrders);

public sealed record OfflineSaleLineDto(string Sku, int Quantity, decimal UnitPrice);

public sealed record OfflineMoneyOrderDto(decimal Amount, string Sender, string Recipient);

public sealed record PushTransactionsResponse(IReadOnlyList<OfflineSaleResultDto> Results);

/// <summary>Outcome for one sale: Accepted, AcceptedWithWarnings or Rejected (Duplicate = this sale was already received).</summary>
public sealed record OfflineSaleResultDto(
    Guid LocalTransactionId,
    string Outcome,
    int? ServerTransactionId,
    string Message,
    IReadOnlyList<string> Warnings,
    bool Duplicate);

/// <summary>GET /api/sync/branch-snapshot?since=... response.</summary>
public sealed record BranchSnapshotResponse(
    int SchemaVersion,
    string BranchCode,
    DateTimeOffset GeneratedAtUtc,
    bool IsDelta,
    IReadOnlyList<ProductSnapshotDto> Products,
    IReadOnlyList<StockSnapshotDto> Stock);

public sealed record ProductSnapshotDto(string Sku, string Name, string Category, decimal UnitPrice, string TaxCategory, bool IsActive);

public sealed record StockSnapshotDto(string Sku, int QuantityOnHand, int ReorderThreshold);

public static class SyncContract
{
    public const int SchemaVersion = 1;
}
