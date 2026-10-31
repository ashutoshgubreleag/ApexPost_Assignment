using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Repositories.Seeding;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Common;
using ApexPost.Services.Sync;
using ApexPost.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApexPost.IntegrationTests;

/// <summary>
/// Calls every query-service method once against SQL Server with realistic data. In-memory fakes
/// cannot reveal LINQ that EF Core fails to translate, wrong column mappings or a stored-procedure
/// result that does not fit its DTO; those only show up here (or in the browser).
/// </summary>
public sealed class QueryTranslationTests
{
    private static readonly BranchCode Br001 = new("BR-001");

    [Fact]
    public async Task Every_query_service_method_runs_on_sql_server()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        await SeedAsync(harness);
        var ct = CancellationToken.None;
        var day = DateOnly.FromDateTime(harness.Clock.Now.UtcDateTime);
        var from = day.AddDays(-30);

        using var scope = harness.NewScope();
        var sp = scope.ServiceProvider;

        var branches = sp.GetRequiredService<IBranchQueries>();
        Assert.Equal(6, (await branches.ListAsync(ct)).Count);
        Assert.NotNull(await branches.GetAsync(Br001, ct));

        var staff = sp.GetRequiredService<IStaffQueries>();
        Assert.NotEmpty(await staff.ListAsync(null, ct));
        Assert.NotEmpty(await staff.ListAsync(Br001, ct));
        Assert.NotNull(await staff.GetAsync(7, ct));

        var catalog = sp.GetRequiredService<ICatalogQueries>();
        Assert.NotEmpty(await catalog.ListProductsAsync(ct));
        Assert.NotEmpty(await catalog.ListZonesAsync(ct));
        Assert.NotEmpty(await catalog.GetStockForBranchAsync(Br001, ct));

        var transactions = sp.GetRequiredService<ITransactionQueries>();
        var recent = await transactions.ListRecentAsync(null, 20, ct);
        Assert.NotEmpty(recent);
        Assert.NotEmpty(await transactions.ListRecentAsync(Br001, 20, ct));
        Assert.NotNull(await transactions.GetSummaryAsync(recent[0].Id, ct));
        Assert.NotNull(await transactions.GetDetailsAsync(recent[0].Id, ct));
        Assert.NotEmpty(await transactions.ListMoneyOrdersAsync(null, 20, ct));
        Assert.NotEmpty(await transactions.ListMoneyOrdersAsync(Br001, 20, ct));

        await using (var db = harness.NewContext())
        {
            var shipment = db.Shipments.OrderBy(s => s.Id).Select(s => new { s.Id, s.TrackingNumber }).First();
            Assert.NotNull(await transactions.GetShipmentAsync(shipment.Id, ct));
            Assert.NotNull(await transactions.GetShipmentByTrackingNumberAsync(shipment.TrackingNumber, ct));
            var moneyOrderId = db.MoneyOrders.OrderBy(m => m.Id).Select(m => m.Id).First();
            Assert.NotNull(await transactions.GetMoneyOrderAsync(moneyOrderId, ct));
        }

        var reports = sp.GetRequiredService<IReportQueries>();
        Assert.NotNull(await reports.GetLatestTradingDayAsync(day, ct));
        Assert.Equal(6, (await reports.GetDailySalesAsync(day.AddDays(-1), null, ct)).Count);
        Assert.Single(await reports.GetDailySalesAsync(day.AddDays(-1), Br001, ct));
        Assert.True((await reports.GetBranchSalesSplitAsync(Br001, day.AddDays(-1), ct)).PaidTransactions > 0);
        Assert.NotEmpty(await reports.GetLowStockAsync(null, 1000, ct));
        Assert.NotEmpty(await reports.GetLowStockAsync(Br001, 1000, ct));
        Assert.NotEmpty(await reports.GetShippingVolumeAsync(from, day, null, ct));
        Assert.NotEmpty(await reports.GetShippingVolumeAsync(from, day, Br001, ct));
        var audit = await reports.GetMoneyOrderAuditAsync(from, day, null, 2500m, ct);
        Assert.NotEmpty(audit.Rows);
        Assert.NotEmpty(audit.MoneyOrders);
        Assert.NotNull(await reports.GetMoneyOrderAuditAsync(from, day, Br001, 2500m, ct));

        var notifications = sp.GetRequiredService<INotificationQueries>();
        Assert.NotNull(await notifications.ListRecentAsync(null, 10, ct));
        Assert.NotEmpty(await notifications.ListRecentAsync(Br001, 10, ct));

        var sync = sp.GetRequiredService<ISyncQueries>();
        var full = await sync.GetBranchSnapshotAsync(Br001, null, ct);
        Assert.NotEmpty(full.Products);
        Assert.NotEmpty(full.Stock);
        Assert.NotNull(await sync.GetBranchSnapshotAsync(Br001, harness.Clock.Now.AddDays(-1), ct));
        Assert.NotEmpty(await sync.ListConflictsAsync(null, 10, ct));
        Assert.NotEmpty(await sync.ListConflictsAsync(Br001, 10, ct));

        var auditStore = sp.GetRequiredService<IAuditStore>();
        Assert.NotNull(await auditStore.ReadRecentAsync(10, ct));
    }

    private static async Task SeedAsync(ServiceHarness harness)
    {
        using (var scope = harness.NewScope())
        {
            var seeder = new HistoricalDataSeeder(
                scope.ServiceProvider.GetRequiredService<ApexPost.Database.ApexPostDbContext>(),
                harness.Get<BusinessRules>(),
                harness.Clock,
                NullLogger<HistoricalDataSeeder>.Instance);
            Assert.True(await seeder.SeedAsync(days: 5, transactionsPerBranchPerDay: 12, CancellationToken.None) > 0);
        }

        // An offline sale far below the server price creates a sync conflict and a notification.
        var result = await harness.Scoped<IOfflineSaleService>().RecordAsync(new RecordOfflineSaleCommand
        {
            DeviceId = "TERM-INT-01",
            LocalTransactionId = Guid.NewGuid(),
            BranchCode = "BR-001",
            CounterNumber = 1,
            ClerkStaffId = 7,
            CapturedAt = harness.Clock.Now.AddHours(-2),
            PaymentMethod = PaymentMethod.Cash,
            Lines = [new OfflineSaleLine { Sku = "PKG-001", Quantity = 1, UnitPrice = 1.00m }],
        }, CancellationToken.None);
        Assert.Equal(SyncOutcome.AcceptedWithWarnings, result.Outcome);
    }
}
