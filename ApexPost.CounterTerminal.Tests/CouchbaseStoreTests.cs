using ApexPost.Contracts;
using ApexPost.CounterTerminal.Domain;
using ApexPost.CounterTerminal.Infrastructure.Couchbase;
using ApexPost.Models.ValueObjects;

namespace ApexPost.CounterTerminal.Tests;

/// <summary>Couchbase Lite persistence: CRUD, parameterized SQL++ queries, reopen, field-level merge.</summary>
public sealed class CouchbaseStoreTests : IDisposable
{
    private readonly TerminalHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public void Catalogue_document_round_trips_by_key_with_money_in_cents()
    {
        _h.SeedCatalogue();

        var item = _h.Catalog.Get(new Sku("STA-001"));

        Assert.NotNull(item);
        Assert.Equal("Stamp book", item.Name);
        Assert.Equal(new Money(12.50m), item.UnitPrice);

        using var raw = _h.Database.Catalog.GetDocument("product::STA-001");
        Assert.NotNull(raw);
        Assert.Equal(1250, raw.GetLong("priceCents"));
        Assert.Equal("product", raw.GetString("type"));
        Assert.Equal(TerminalDatabase.SchemaVersion, raw.GetInt("schemaVersion"));
    }

    [Fact]
    public void Upsert_replaces_server_owned_catalogue_values()
    {
        _h.SeedCatalogue();

        _h.Catalog.Upsert([new CatalogItem(new Sku("STA-001"), "Stamp book (12)", "Stationery", new Money(13.00m), "Standard", true)]);

        Assert.Equal(new Money(13.00m), _h.Catalog.Get(new Sku("STA-001"))!.UnitPrice);
    }

    [Fact]
    public void Sqlpp_category_query_returns_active_products_sorted_by_name()
    {
        _h.SeedCatalogue();

        var names = _h.Catalog.ListByCategory("Stationery").Select(p => p.Name).ToList();

        Assert.Equal(["Envelope pack", "Stamp book"], names);
    }

    [Fact]
    public void Sqlpp_parameters_are_not_interpreted_as_query_text()
    {
        _h.SeedCatalogue();

        Assert.Empty(_h.Catalog.ListByCategory("Stationery' OR '1'='1"));
    }

    [Fact]
    public void Data_survives_closing_and_reopening_the_database()
    {
        _h.SeedCatalogue();
        var sale = _h.Terminal.Sell([("STA-001", 2)], "Cash");

        _h.Reopen();

        Assert.NotNull(_h.Catalog.Get(new Sku("PKG-002")));
        var reloaded = _h.Sales.Get(_h.Options.DeviceId, sale.LocalTransactionId);
        Assert.NotNull(reloaded);
        Assert.Equal(SaleStatus.Pending, reloaded.Status);
        Assert.Equal(sale.Total, reloaded.Total);
        Assert.Equal(2, _h.Stock.Get(_h.Branch, new Sku("STA-001"))!.LocalSoldSinceSync);
    }

    [Fact]
    public void Pending_older_than_query_uses_the_capture_time()
    {
        _h.SeedCatalogue();
        var old = _h.Terminal.Sell([("STA-002", 1)], "Cash");
        _h.Clock.Advance(TimeSpan.FromMinutes(20));
        _h.Terminal.Sell([("STA-002", 1)], "Cash");

        var stuck = _h.Terminal.StuckSales(TimeSpan.FromMinutes(15));

        Assert.Equal([old.LocalTransactionId], stuck.Select(s => s.LocalTransactionId));
    }

    [Fact]
    public void Todays_totals_aggregate_local_sales_for_the_business_date()
    {
        _h.SeedCatalogue();
        _h.Terminal.Sell([("STA-001", 2)], "Cash");       // 25.00
        _h.Terminal.Sell([("PKG-002", 1)], "Card");     //  3.20
        _h.Clock.Advance(TimeSpan.FromDays(1));
        _h.Terminal.Sell([("STA-002", 1)], "Cash");       // tomorrow

        var (count, total) = _h.Sales.TodaysTotals(DateOnly.FromDateTime(TerminalHarness.Start.UtcDateTime));

        Assert.Equal(2, count);
        Assert.Equal(new Money(28.20m), total);
    }

    [Fact]
    public void Pulled_stock_replaces_server_fields_but_keeps_device_owned_counters()
    {
        _h.SeedCatalogue(stampQty: 10);
        _h.Terminal.Sell([("STA-001", 3)], "Cash");

        _h.Stock.ApplyServerSnapshot(_h.Branch, [new StockSnapshotDto("STA-001", 40, 4)], TerminalHarness.Start.AddHours(1));

        var level = _h.Stock.Get(_h.Branch, new Sku("STA-001"))!;
        Assert.Equal(40, level.ServerQuantity);
        Assert.Equal(4, level.ReorderThreshold);
        Assert.Equal(3, level.LocalSoldSinceSync);
        Assert.Equal(37, level.Available);
    }

    [Fact]
    public void Sale_documents_hold_no_identity_or_card_data()
    {
        _h.SyncState.Save(_h.SyncState.Get() with { IsOnline = false });
        _h.SeedCatalogue();
        var sale = _h.Terminal.Sell([("STA-001", 1)], "Card", [new PendingMoneyOrder(new Money(150m), "Ada Fictional", "Bo Example")]);

        using var raw = _h.Database.Pending.GetDocument(sale.DocumentId);
        var keys = raw!.Keys.ToList();

        Assert.DoesNotContain(keys, k => k.Contains("document", StringComparison.OrdinalIgnoreCase) || k.Contains("card", StringComparison.OrdinalIgnoreCase) || k.Contains("identity", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("sale", raw.GetString("type"));
        Assert.Equal(15000, raw.GetArray("moneyOrders")!.GetDictionary(0)!.GetLong("amountCents"));
    }
}
