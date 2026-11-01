using ApexPost.CounterTerminal.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;

namespace ApexPost.CounterTerminal.Tests;

/// <summary>Counter rules that hold with no network at all.</summary>
public sealed class TerminalServiceTests : IDisposable
{
    private readonly TerminalHarness _h = new(online: false);

    public TerminalServiceTests() => _h.SeedCatalogue(stampQty: 5, boxQty: 3);

    public void Dispose() => _h.Dispose();

    [Fact]
    public void Offline_sale_is_saved_to_the_outbox_and_reduces_local_stock()
    {
        var sale = _h.Terminal.Sell([("STA-001", 2), ("PKG-002", 1)], "Cash");

        Assert.Equal(SaleStatus.Pending, sale.Status);
        Assert.Equal(new Money(28.20m), sale.Total);
        Assert.Equal(1, _h.Terminal.Status().PendingCount);
        Assert.Equal(3, _h.Stock.Get(_h.Branch, new Sku("STA-001"))!.Available);
        Assert.Equal(2, _h.Stock.Get(_h.Branch, new Sku("PKG-002"))!.Available);
    }

    [Fact]
    public void Offline_sale_uses_the_local_snapshot_price()
    {
        var sale = _h.Terminal.Sell([("STA-001", 1)], "Card");

        Assert.Equal(new Money(12.50m), sale.Lines.Single().UnitPrice);
    }

    [Fact]
    public void Local_stock_cannot_go_negative_across_several_offline_sales()
    {
        _h.Terminal.Sell([("PKG-002", 2)], "Cash");

        var ex = Assert.Throws<BusinessRuleViolationException>(() => _h.Terminal.Sell([("PKG-002", 2)], "Cash"));

        Assert.Equal("STOCK_INSUFFICIENT", ex.RuleCode);
        Assert.Equal(1, _h.Terminal.Status().PendingCount);
    }

    [Fact]
    public void Repeated_skus_in_one_sale_are_combined_before_the_stock_check()
    {
        Assert.Throws<BusinessRuleViolationException>(() => _h.Terminal.Sell([("PKG-002", 2), ("pkg-002", 2)], "Cash"));
    }

    [Fact]
    public void High_value_money_order_is_blocked_offline()
    {
        var ex = Assert.Throws<BusinessRuleViolationException>(() =>
            _h.Terminal.Sell([], "Cash", [new PendingMoneyOrder(new Money(1500m), "Ada Fictional", "Bo Example")]));

        Assert.Equal("MONEY_ORDER_BLOCKED_OFFLINE", ex.RuleCode);
        Assert.Equal(0, _h.Terminal.Status().PendingCount);
    }

    [Fact]
    public void Low_value_money_order_is_allowed_offline()
    {
        var sale = _h.Terminal.Sell([], "Cash", [new PendingMoneyOrder(new Money(200m), "Ada Fictional", "Bo Example")]);

        Assert.Equal(new Money(200m), sale.Total);
    }

    [Fact]
    public void High_value_money_order_needs_a_full_counter_even_online()
    {
        _h.SyncState.Save(_h.SyncState.Get() with { IsOnline = true });

        var ex = Assert.Throws<BusinessRuleViolationException>(() =>
            _h.Terminal.Sell([], "Cash", [new PendingMoneyOrder(new Money(1500m), "Ada Fictional", "Bo Example")]));

        Assert.Equal("MONEY_ORDER_ID_REQUIRED", ex.RuleCode);
    }

    [Theory]
    [InlineData("NOPE-1", "Cash")]
    [InlineData("STA-009", "Cash")]
    [InlineData("STA-001", "Cheque")]
    public void Unknown_inactive_or_badly_paid_sales_are_refused(string sku, string payment)
    {
        Assert.Throws<CommandValidationException>(() => _h.Terminal.Sell([(sku, 1)], payment));
    }

    [Fact]
    public void Local_transaction_ids_are_unique_without_the_server()
    {
        var ids = Enumerable.Range(0, 20).Select(_ => _h.Terminal.Sell([("STA-002", 1)], "Cash").DocumentId).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.StartsWith("tx::TERM-T-01::", id, StringComparison.Ordinal));
    }
}
