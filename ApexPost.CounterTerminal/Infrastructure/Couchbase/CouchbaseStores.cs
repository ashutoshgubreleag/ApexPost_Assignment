using System.Globalization;
using ApexPost.Contracts;
using ApexPost.CounterTerminal.Application;
using ApexPost.CounterTerminal.Domain;
using ApexPost.Models.ValueObjects;
using Couchbase.Lite;
using Couchbase.Lite.Query;

namespace ApexPost.CounterTerminal.Infrastructure.Couchbase;

/*
 * Couchbase Lite repositories. Documents are JSON with a "type" and a "schemaVersion"; money is
 * stored as integer cents (Couchbase Lite has no decimal type); times are ISO-8601 UTC strings so
 * they sort and compare correctly in SQL++. Every SQL++ query is parameterized ($name).
 * No identity document numbers or card data are ever written to these documents.
 */

public sealed class CouchbaseCatalogStore(TerminalDatabase db) : ICatalogStore
{
    public CatalogItem? Get(Sku sku)
    {
        using var doc = db.Catalog.GetDocument(TerminalIds.Product(sku));
        return doc is null ? null : Map(doc.GetString("sku")!, doc.GetString("name"), doc.GetString("category"), doc.GetLong("priceCents"), doc.GetString("taxCategory"), doc.GetBoolean("isActive"));
    }

    public IReadOnlyList<CatalogItem> ListByCategory(string category)
    {
        using var query = db.CreateQuery(
            "SELECT sku, name, category, priceCents, taxCategory, isActive FROM apexpost.catalog " +
            "WHERE type = 'product' AND category = $category AND isActive = true ORDER BY name");
        query.Parameters = new Parameters().SetString("category", category);
        return query.Execute().AllResults()
            .Select(r => Map(r.GetString("sku")!, r.GetString("name"), r.GetString("category"), r.GetLong("priceCents"), r.GetString("taxCategory"), r.GetBoolean("isActive")))
            .ToList();
    }

    public IReadOnlyList<string> Categories()
    {
        using var query = db.CreateQuery("SELECT DISTINCT category FROM apexpost.catalog WHERE type = 'product' ORDER BY category");
        return query.Execute().AllResults().Select(r => r.GetString("category") ?? string.Empty).Where(c => c.Length > 0).ToList();
    }

    public void Upsert(IEnumerable<CatalogItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            using var doc = new MutableDocument(TerminalIds.Product(item.Sku));
            doc.SetString("type", "product")
                .SetInt("schemaVersion", TerminalDatabase.SchemaVersion)
                .SetString("sku", item.Sku.Value)
                .SetString("name", item.Name)
                .SetString("category", item.Category)
                .SetLong("priceCents", Cents.From(item.UnitPrice))
                .SetString("taxCategory", item.TaxCategory)
                .SetBoolean("isActive", item.IsActive);
            db.Catalog.Save(doc);
        }
    }

    private static CatalogItem Map(string sku, string? name, string? category, long cents, string? tax, bool active) =>
        new(new Sku(sku), name ?? sku, category ?? string.Empty, Cents.ToMoney(cents), tax ?? "Standard", active);
}

public sealed class CouchbaseStockStore(TerminalDatabase db) : IStockStore
{
    public StockLevel? Get(BranchCode branch, Sku sku)
    {
        using var doc = db.Stock.GetDocument(TerminalIds.Stock(branch, sku));
        return doc is null ? null : Map(doc);
    }

    public IReadOnlyList<StockLevel> List(BranchCode branch)
    {
        using var query = db.CreateQuery("SELECT META().id AS id FROM apexpost.stock WHERE type = 'stock' AND branchCode = $branch ORDER BY sku");
        query.Parameters = new Parameters().SetString("branch", branch.Value);
        var ids = query.Execute().AllResults().Select(r => r.GetString("id")!).ToList();
        return ids.Select(id =>
        {
            using var doc = db.Stock.GetDocument(id);
            return doc is null ? null : Map(doc);
        }).OfType<StockLevel>().ToList();
    }

    public void Save(StockLevel stock)
    {
        ArgumentNullException.ThrowIfNull(stock);
        using var existing = db.Stock.GetDocument(TerminalIds.Stock(stock.BranchCode, stock.Sku));
        using var doc = existing?.ToMutable() ?? new MutableDocument(TerminalIds.Stock(stock.BranchCode, stock.Sku));
        Write(doc, stock);
        db.Stock.Save(doc);
    }

    /// <summary>
    /// Field-level merge (CONFLICTS.md C3): server-owned fields are replaced by the pulled values;
    /// device-owned fields (localSoldSinceSync, lastLocalSaleAt) are kept exactly as they are.
    /// </summary>
    public void ApplyServerSnapshot(BranchCode branch, IEnumerable<StockSnapshotDto> rows, DateTimeOffset syncedAt)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach (var row in rows)
        {
            var sku = new Sku(row.Sku);
            var local = Get(branch, sku);
            Save(StockMerge.Apply(local, branch, sku, row.QuantityOnHand, row.ReorderThreshold, syncedAt));
        }
    }

    private static void Write(MutableDocument doc, StockLevel s)
    {
        doc.SetString("type", "stock")
            .SetInt("schemaVersion", TerminalDatabase.SchemaVersion)
            .SetString("branchCode", s.BranchCode.Value)
            .SetString("sku", s.Sku.Value)
            .SetInt("serverQuantity", s.ServerQuantity)
            .SetInt("reorderThreshold", s.ReorderThreshold)
            .SetString("serverSyncedAt", Iso.From(s.ServerSyncedAt))
            .SetInt("localSoldSinceSync", s.LocalSoldSinceSync)
            .SetString("lastLocalSaleAt", Iso.From(s.LastLocalSaleAt));
    }

    private static StockLevel Map(Document doc) => new(
        new BranchCode(doc.GetString("branchCode")!),
        new Sku(doc.GetString("sku")!),
        doc.GetInt("serverQuantity"),
        doc.GetInt("reorderThreshold"),
        Iso.Parse(doc.GetString("serverSyncedAt")),
        doc.GetInt("localSoldSinceSync"),
        Iso.Parse(doc.GetString("lastLocalSaleAt")));
}

/// <summary>The field-ownership rule for stock documents, kept pure so it is unit-tested on its own.</summary>
public static class StockMerge
{
    public static StockLevel Apply(StockLevel? local, BranchCode branch, Sku sku, int serverQuantity, int reorderThreshold, DateTimeOffset syncedAt) =>
        local is null
            ? new StockLevel(branch, sku, serverQuantity, reorderThreshold, syncedAt, 0, null)
            : local with { ServerQuantity = serverQuantity, ReorderThreshold = reorderThreshold, ServerSyncedAt = syncedAt };
}

public sealed class CouchbasePendingSaleStore(TerminalDatabase db) : IPendingSaleStore
{
    public PendingSale? Get(string deviceId, Guid localTransactionId)
    {
        using var doc = db.Pending.GetDocument(TerminalIds.Transaction(deviceId, localTransactionId));
        return doc is null ? null : Map(doc);
    }

    public void Save(PendingSale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        using var doc = new MutableDocument(sale.DocumentId);
        var lines = new MutableArrayObject();
        foreach (var line in sale.Lines)
        {
            var entry = new MutableDictionaryObject();
            entry.SetString("sku", line.Sku.Value)
                .SetString("name", line.Name)
                .SetInt("quantity", line.Quantity)
                .SetLong("unitPriceCents", Cents.From(line.UnitPrice));
            lines.AddDictionary(entry);
        }

        var orders = new MutableArrayObject();
        foreach (var order in sale.MoneyOrders)
        {
            var entry = new MutableDictionaryObject();
            entry.SetLong("amountCents", Cents.From(order.Amount))
                .SetString("sender", order.Sender)
                .SetString("recipient", order.Recipient);
            orders.AddDictionary(entry);
        }

        var warnings = new MutableArrayObject();
        foreach (var warning in sale.Warnings)
        {
            warnings.AddString(warning);
        }

        doc.SetString("type", "sale")
            .SetInt("schemaVersion", TerminalDatabase.SchemaVersion)
            .SetString("localTransactionId", sale.LocalTransactionId.ToString("D"))
            .SetString("deviceId", sale.DeviceId)
            .SetString("branchCode", sale.BranchCode.Value)
            .SetInt("counterNumber", sale.CounterNumber)
            .SetInt("clerkStaffId", sale.ClerkStaffId)
            .SetString("capturedAt", Iso.From(sale.CapturedAt))
            .SetString("businessDate", sale.CapturedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .SetString("paymentMethod", sale.PaymentMethod)
            .SetArray("lines", lines)
            .SetArray("moneyOrders", orders)
            .SetLong("totalCents", Cents.From(sale.Total))
            .SetString("status", sale.Status.ToString())
            .SetInt("attempts", sale.Attempts)
            .SetString("lastAttemptAt", Iso.From(sale.LastAttemptAt))
            .SetString("lastError", sale.LastError)
            .SetArray("warnings", warnings);
        if (sale.ServerTransactionId is int serverId)
        {
            doc.SetInt("serverTransactionId", serverId);
        }

        db.Pending.Save(doc);
    }

    public IReadOnlyList<PendingSale> ListByStatus(SaleStatus status, int take)
    {
        using var query = db.CreateQuery(
            "SELECT META().id AS id FROM apexpost.pendingTransactions " +
            "WHERE type = 'sale' AND status = $status ORDER BY capturedAt LIMIT $take");
        query.Parameters = new Parameters().SetString("status", status.ToString()).SetInt("take", take);
        return Load(query);
    }

    public IReadOnlyList<PendingSale> ListPendingOlderThan(TimeSpan age, DateTimeOffset now)
    {
        using var query = db.CreateQuery(
            "SELECT META().id AS id FROM apexpost.pendingTransactions " +
            "WHERE type = 'sale' AND status = 'Pending' AND capturedAt < $cutoff ORDER BY capturedAt");
        query.Parameters = new Parameters().SetString("cutoff", Iso.From(now - age));
        return Load(query);
    }

    public (int Count, Money Total) TodaysTotals(DateOnly today)
    {
        using var query = db.CreateQuery(
            "SELECT COUNT(*) AS salesCount, SUM(totalCents) AS totalCents FROM apexpost.pendingTransactions " +
            "WHERE type = 'sale' AND businessDate = $today AND status != 'Rejected' AND status != 'Invalid'");
        query.Parameters = new Parameters().SetString("today", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var row = query.Execute().AllResults().FirstOrDefault();
        return row is null ? (0, Money.Zero) : (row.GetInt("salesCount"), Cents.ToMoney(row.GetLong("totalCents")));
    }

    public int CountPending()
    {
        using var query = db.CreateQuery("SELECT COUNT(*) AS pending FROM apexpost.pendingTransactions WHERE type = 'sale' AND status = 'Pending'");
        return query.Execute().AllResults().FirstOrDefault()?.GetInt("pending") ?? 0;
    }

    private List<PendingSale> Load(IQuery query) =>
        query.Execute().AllResults()
            .Select(r => r.GetString("id")!)
            .Select(id =>
            {
                using var doc = db.Pending.GetDocument(id);
                return doc is null ? null : Map(doc);
            })
            .OfType<PendingSale>()
            .ToList();

    private static PendingSale Map(Document doc)
    {
        var lines = doc.GetArray("lines")?.Select(o => (DictionaryObject)o!)
            .Select(d => new SaleLine(new Sku(d.GetString("sku")!), d.GetString("name") ?? string.Empty, d.GetInt("quantity"), Cents.ToMoney(d.GetLong("unitPriceCents"))))
            .ToList() ?? [];
        var orders = doc.GetArray("moneyOrders")?.Select(o => (DictionaryObject)o!)
            .Select(d => new PendingMoneyOrder(Cents.ToMoney(d.GetLong("amountCents")), d.GetString("sender") ?? string.Empty, d.GetString("recipient") ?? string.Empty))
            .ToList() ?? [];
        var warnings = doc.GetArray("warnings")?.Select(o => o?.ToString() ?? string.Empty).ToList() ?? [];

        return new PendingSale(
            Guid.Parse(doc.GetString("localTransactionId")!),
            doc.GetString("deviceId")!,
            new BranchCode(doc.GetString("branchCode")!),
            doc.GetInt("counterNumber"),
            doc.GetInt("clerkStaffId"),
            Iso.Parse(doc.GetString("capturedAt")) ?? DateTimeOffset.MinValue,
            doc.GetString("paymentMethod") ?? "Cash",
            lines,
            orders,
            Enum.TryParse<SaleStatus>(doc.GetString("status"), out var status) ? status : SaleStatus.Pending,
            doc.GetInt("attempts"),
            Iso.Parse(doc.GetString("lastAttemptAt")),
            doc.GetString("lastError"),
            doc.Contains("serverTransactionId") ? doc.GetInt("serverTransactionId") : null,
            warnings);
    }
}

public sealed class CouchbaseSyncStateStore(TerminalDatabase db, TerminalOptions options) : ISyncStateStore
{
    public SyncState Get()
    {
        using var doc = db.SyncState.GetDocument(TerminalIds.SyncState(options.DeviceId));
        return doc is null
            ? new SyncState(options.DeviceId, new BranchCode(options.BranchCode), !options.ForceOffline, null, null, null, null)
            : new SyncState(
                options.DeviceId,
                new BranchCode(doc.GetString("branchCode") ?? options.BranchCode),
                doc.GetBoolean("isOnline"),
                Iso.Parse(doc.GetString("lastSuccessfulSyncAt")),
                Iso.Parse(doc.GetString("lastSnapshotAt")),
                doc.GetString("lastError"),
                Iso.Parse(doc.GetString("lastErrorAt")));
    }

    public void Save(SyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        using var doc = new MutableDocument(TerminalIds.SyncState(state.DeviceId));
        doc.SetString("type", "syncState")
            .SetInt("schemaVersion", TerminalDatabase.SchemaVersion)
            .SetString("deviceId", state.DeviceId)
            .SetString("branchCode", state.BranchCode.Value)
            .SetBoolean("isOnline", state.IsOnline)
            .SetString("lastSuccessfulSyncAt", Iso.From(state.LastSuccessfulSyncAt))
            .SetString("lastSnapshotAt", Iso.From(state.LastSnapshotAt))
            .SetString("lastError", state.LastError)
            .SetString("lastErrorAt", Iso.From(state.LastErrorAt));
        db.SyncState.Save(doc);
    }
}

internal static class Cents
{
    public static long From(Money money) => (long)Math.Round(money.Amount * 100m, MidpointRounding.AwayFromZero);

    public static Money ToMoney(long cents) => new(cents / 100m);
}

internal static class Iso
{
    public static string? From(DateTimeOffset? value) =>
        value?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    public static DateTimeOffset? Parse(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
}
