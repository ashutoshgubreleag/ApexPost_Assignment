using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Sync;
using ApexPost.Services.Transactions;
using ApexPost.Tests.Support;
using ApexPost.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Tests.Unit;

/// <summary>
/// Week 3 application-layer rules with fakes: one SaveChanges per use case, nothing saved on
/// failure, offline sales idempotent, and conflict policies C1 (price) / C2 (stock) / C3 (server-owned fields).
/// </summary>
public sealed class OfflineSyncAndUnitOfWorkTests
{
    private const string Device = "TERM-TEST-01";

    [Fact]
    public async Task Paying_a_sale_commits_exactly_once()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var clerk = ServiceHarness.Clerk();
        var tx = await harness.Scoped<ITransactionService>().OpenAsync(clerk, 1, CancellationToken.None);
        await harness.Scoped<ITransactionService>().AddRetailItemAsync(tx.Id, new AddRetailItemCommand { Sku = "PKG-001", Quantity = 2 }, clerk, CancellationToken.None);

        using var scope = harness.NewScope();
        var spy = scope.ServiceProvider.GetRequiredService<SpyUnitOfWork>();
        await scope.ServiceProvider.GetRequiredService<ITransactionService>().PayAsync(tx.Id, PaymentMethod.Cash, clerk, CancellationToken.None);

        Assert.Equal(1, spy.SaveCalls);
    }

    [Fact]
    public async Task A_failed_use_case_never_commits()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var clerk = ServiceHarness.Clerk();
        var tx = await harness.Scoped<ITransactionService>().OpenAsync(clerk, 1, CancellationToken.None);
        var commitsBefore = harness.Store.CommitCount;

        using var scope = harness.NewScope();
        var spy = scope.ServiceProvider.GetRequiredService<SpyUnitOfWork>();
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            scope.ServiceProvider.GetRequiredService<ITransactionService>().AddRetailItemAsync(tx.Id, new AddRetailItemCommand { Sku = "PKG-001", Quantity = 100 }, clerk, CancellationToken.None));

        Assert.Equal(0, spy.SaveCalls);
        Assert.Equal(commitsBefore, harness.Store.CommitCount);
    }

    [Fact]
    public async Task Offline_sale_is_recorded_once_and_a_retry_returns_the_same_result()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var command = Sale(("PKG-001", 2, 2.95m));
        var stockBefore = Stock(harness, "BR-001", "PKG-001");

        var first = await harness.Scoped<IOfflineSaleService>().RecordAsync(command, CancellationToken.None);
        var retry = await harness.Scoped<IOfflineSaleService>().RecordAsync(command, CancellationToken.None);

        Assert.Equal(SyncOutcome.Accepted, first.Outcome);
        Assert.False(first.WasDuplicate);
        Assert.True(retry.WasDuplicate);
        Assert.Equal(first.ServerTransactionId, retry.ServerTransactionId);
        Assert.Single(harness.Store.All<SyncInboxEntry>());
        Assert.Single(harness.Store.All<CounterTransaction>(), t => t.Source == TransactionSource.OfflineTerminal);
        Assert.Equal(stockBefore - 2, Stock(harness, "BR-001", "PKG-001"));
    }

    [Fact]
    public async Task Offline_sale_is_one_commit_including_inbox_row_transaction_and_stock()
    {
        await using var harness = await ServiceHarness.CreateAsync();

        using var scope = harness.NewScope();
        var spy = scope.ServiceProvider.GetRequiredService<SpyUnitOfWork>();
        await scope.ServiceProvider.GetRequiredService<IOfflineSaleService>().RecordAsync(Sale(("PKG-001", 1, 2.95m)), CancellationToken.None);

        Assert.Equal(1, spy.SaveCalls);
    }

    [Fact]
    public async Task C1_price_changed_while_offline_is_honoured_and_flagged_above_tolerance()
    {
        await using var harness = await ServiceHarness.CreateAsync();

        // Server price 2.95; the terminal still had 2.50 (15% lower).
        var result = await harness.Scoped<IOfflineSaleService>().RecordAsync(Sale(("PKG-001", 1, 2.50m)), CancellationToken.None);

        Assert.Equal(SyncOutcome.AcceptedWithWarnings, result.Outcome);
        var tx = harness.Store.All<CounterTransaction>().Single(t => t.Id == result.ServerTransactionId);
        Assert.Equal(new Money(2.50m), tx.LineItems.Single().UnitPrice);
        var conflict = Assert.Single(harness.Store.All<SyncInboxEntry>().Single().Conflicts);
        Assert.Equal(SyncConflictType.PriceVariance, conflict.Type);
    }

    [Fact]
    public async Task C1_small_price_difference_is_accepted_without_a_warning()
    {
        await using var harness = await ServiceHarness.CreateAsync();

        var result = await harness.Scoped<IOfflineSaleService>().RecordAsync(Sale(("PKG-001", 1, 2.90m)), CancellationToken.None);

        Assert.Equal(SyncOutcome.Accepted, result.Outcome);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task C2_offline_sales_beyond_stock_are_accepted_stock_stops_at_zero_and_shortfall_is_flagged()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var onHand = Stock(harness, "BR-001", "PKG-001");

        // Two terminals both sold the last units while offline.
        var a = await harness.Scoped<IOfflineSaleService>().RecordAsync(Sale(("PKG-001", onHand - 1, 2.95m)), CancellationToken.None);
        var b = await harness.Scoped<IOfflineSaleService>().RecordAsync(Sale(("PKG-001", 3, 2.95m)) with { DeviceId = "TERM-TEST-03" }, CancellationToken.None);

        Assert.Equal(SyncOutcome.Accepted, a.Outcome);
        Assert.Equal(SyncOutcome.AcceptedWithWarnings, b.Outcome);
        Assert.Equal(0, Stock(harness, "BR-001", "PKG-001"));
        var conflict = harness.Store.All<SyncInboxEntry>().SelectMany(e => e.Conflicts).Single();
        Assert.Equal(SyncConflictType.StockShortfall, conflict.Type);
        Assert.Contains("2 unit(s)", conflict.Details, StringComparison.Ordinal);
        Assert.Contains(harness.Store.All<Notification>(), n => n.Type == NotificationType.SyncConflict);
    }

    [Fact]
    public async Task Offline_sale_from_a_clerk_of_another_branch_is_rejected_without_a_transaction()
    {
        await using var harness = await ServiceHarness.CreateAsync();

        var result = await harness.Scoped<IOfflineSaleService>().RecordAsync(Sale(("PKG-001", 1, 2.95m)) with { ClerkStaffId = 10 }, CancellationToken.None);

        Assert.Equal(SyncOutcome.Rejected, result.Outcome);
        Assert.Null(result.ServerTransactionId);
        Assert.DoesNotContain(harness.Store.All<CounterTransaction>(), t => t.Source == TransactionSource.OfflineTerminal);
    }

    [Fact]
    public async Task High_value_money_order_from_an_offline_device_is_rejected()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var command = Sale() with { MoneyOrders = [new OfflineMoneyOrder { Amount = 1500m, Sender = "Ada Fictional", Recipient = "Bo Example" }] };

        var result = await harness.Scoped<IOfflineSaleService>().RecordAsync(command, CancellationToken.None);

        Assert.Equal(SyncOutcome.Rejected, result.Outcome);
        Assert.Empty(harness.Store.All<MoneyOrder>());
    }

    [Fact]
    public async Task C3_server_derives_totals_and_status_the_device_cannot_set_them()
    {
        await using var harness = await ServiceHarness.CreateAsync();

        var result = await harness.Scoped<IOfflineSaleService>().RecordAsync(Sale(("PKG-001", 2, 2.95m), ("PKG-002", 1, 3.95m)), CancellationToken.None);

        var tx = harness.Store.All<CounterTransaction>().Single(t => t.Id == result.ServerTransactionId);
        Assert.Equal(TransactionStatus.Paid, tx.Status);
        Assert.Equal(TransactionSource.OfflineTerminal, tx.Source);
        Assert.Equal(new Money(9.85m), tx.LineItems.Select(l => l.UnitPrice * l.Quantity).Total());
        Assert.True(tx.TaxAmount > Money.Zero);
    }

    [Fact]
    public async Task Missing_local_transaction_id_is_a_validation_error()
    {
        await using var harness = await ServiceHarness.CreateAsync();

        await Assert.ThrowsAsync<CommandValidationException>(() =>
            harness.Scoped<IOfflineSaleService>().RecordAsync(Sale(("PKG-001", 1, 2.95m)) with { LocalTransactionId = Guid.Empty }, CancellationToken.None));
    }

    private static RecordOfflineSaleCommand Sale(params (string Sku, int Quantity, decimal Price)[] lines) => new()
    {
        DeviceId = Device,
        LocalTransactionId = Guid.NewGuid(),
        BranchCode = "BR-001",
        CounterNumber = 1,
        ClerkStaffId = 7,
        CapturedAt = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.Zero),
        PaymentMethod = PaymentMethod.Cash,
        Lines = lines.Select(l => new OfflineSaleLine { Sku = l.Sku, Quantity = l.Quantity, UnitPrice = l.Price }).ToList(),
    };

    private static int Stock(ServiceHarness harness, string branch, string sku) =>
        harness.Store.All<BranchStock>().Single(s => s.BranchCode == new BranchCode(branch) && s.Sku == new Sku(sku)).QuantityOnHand;
}
