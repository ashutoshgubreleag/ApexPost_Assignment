using ApexPost.CounterTerminal.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;

namespace ApexPost.CounterTerminal.Application;

/// <summary>
/// Counter use cases that work with or without a network. A sale is validated against the local
/// catalogue and stock snapshot, saved to the local outbox (pendingTransactions) and the local stock
/// is reduced, all before any network call: losing the network can never lose a sale.
/// </summary>
public sealed class TerminalService(
    ICatalogStore catalog,
    IStockStore stock,
    IPendingSaleStore sales,
    ISyncStateStore syncState,
    TerminalOptions options,
    TimeProvider clock)
{
    private static readonly string[] PaymentMethods = ["Cash", "Card", "MobileWallet"];

    public BranchCode Branch { get; } = new(options.BranchCode);

    public IReadOnlyList<string> Categories() => catalog.Categories();

    public IReadOnlyList<CatalogItem> ProductsIn(string category) => catalog.ListByCategory(category);

    public CatalogItem? FindProduct(string sku) => Sku.TryParse(sku, out var parsed) ? catalog.Get(parsed!.Value) : null;

    public IReadOnlyList<StockLevel> Stock() => stock.List(Branch);

    public PendingSale? FindSale(Guid localTransactionId) => sales.Get(options.DeviceId, localTransactionId);

    public IReadOnlyList<PendingSale> StuckSales(TimeSpan olderThan) => sales.ListPendingOlderThan(olderThan, clock.GetUtcNow());

    public (int Count, Money Total) TodaysSales() => sales.TodaysTotals(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));

    public TerminalStatus Status()
    {
        var state = syncState.Get();
        return new TerminalStatus(state.IsOnline, sales.CountPending(), state.LastSuccessfulSyncAt, state.LastError, state.LastErrorAt);
    }

    /// <summary>Sells goods (and optionally low-value money orders) at the counter, online or offline.</summary>
    public PendingSale Sell(IReadOnlyList<(string Sku, int Quantity)> items, string paymentMethod, IReadOnlyList<PendingMoneyOrder>? moneyOrders = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        moneyOrders ??= [];
        if (items.Count == 0 && moneyOrders.Count == 0)
        {
            throw new CommandValidationException("items", "Add at least one item.");
        }

        var method = PaymentMethods.FirstOrDefault(m => string.Equals(m, paymentMethod, StringComparison.OrdinalIgnoreCase))
            ?? throw new CommandValidationException(nameof(paymentMethod), "Payment method must be Cash, Card or MobileWallet.");

        var threshold = new Money(options.MoneyOrderIdentityThreshold);
        var online = syncState.Get().IsOnline;
        foreach (var order in moneyOrders)
        {
            TerminalRules.EnsureMoneyOrderAllowed(order.Amount, threshold, online);
        }

        var now = clock.GetUtcNow();
        var lines = new List<SaleLine>();
        var touched = new List<StockLevel>();
        foreach (var group in items.GroupBy(i => i.Sku.Trim().ToUpperInvariant()))
        {
            var quantity = group.Sum(i => i.Quantity);
            if (quantity <= 0 || !Sku.TryParse(group.Key, out var parsed))
            {
                throw new CommandValidationException("items", $"'{group.Key}' x {quantity} is not a valid line.");
            }

            var sku = parsed!.Value;
            var product = catalog.Get(sku) is { IsActive: true } p
                ? p
                : throw new CommandValidationException("items", $"{sku} is not in this counter's catalogue. Sync to refresh it.");

            var level = stock.Get(Branch, sku);
            TerminalRules.EnsureStockAvailable(level, sku, quantity);

            // C1: the customer pays the price on the local snapshot; the server honours it.
            lines.Add(new SaleLine(sku, product.Name, quantity, product.UnitPrice));
            touched.Add(level! with { LocalSoldSinceSync = level.LocalSoldSinceSync + quantity, LastLocalSaleAt = now });
        }

        var sale = new PendingSale(
            Guid.NewGuid(), options.DeviceId, Branch, options.CounterNumber, options.ClerkStaffId, now, method,
            lines, moneyOrders, SaleStatus.Pending, 0, null, null, null, []);

        // Outbox first, then the local stock: both are local writes, no network involved.
        sales.Save(sale);
        foreach (var level in touched)
        {
            stock.Save(level);
        }

        return sale;
    }
}

public sealed record TerminalStatus(bool IsOnline, int PendingCount, DateTimeOffset? LastSuccessfulSyncAt, string? LastError, DateTimeOffset? LastErrorAt);
