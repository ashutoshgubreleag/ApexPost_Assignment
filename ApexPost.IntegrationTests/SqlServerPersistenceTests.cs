using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;
using ApexPost.Repositories.Queries;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Inventory;
using ApexPost.Services.Sync;
using ApexPost.Services.Transactions;
using ApexPost.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace ApexPost.IntegrationTests;

/// <summary>EF Core + SQL Server behaviour that fakes cannot prove: schema, mapping, transactions, concurrency, procedures, SQL shape.</summary>
public sealed class SqlServerPersistenceTests(ITestOutputHelper output)
{
    private static readonly BranchCode Br001 = new("BR-001");
    private static readonly Sku Pkg001 = new("PKG-001");

    [Fact]
    public async Task Migrations_are_all_applied_and_match_the_model()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        await using var db = harness.NewContext();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(6, await db.Branches.CountAsync());
        Assert.Equal(32, await db.Products.CountAsync());
    }

    [Fact]
    public async Task Paid_sale_round_trips_as_one_aggregate_and_reduces_stock()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var clerk = ServiceHarness.Clerk();
        var before = await StockAsync(harness, Pkg001);

        var tx = await harness.Scoped<ITransactionService>().OpenAsync(clerk, 1, CancellationToken.None);
        await harness.Scoped<ITransactionService>().AddRetailItemAsync(tx.Id, new AddRetailItemCommand { Sku = "PKG-001", Quantity = 2 }, clerk, CancellationToken.None);
        await harness.Scoped<ITransactionService>().PayAsync(tx.Id, PaymentMethod.Card, clerk, CancellationToken.None);

        await using var db = harness.NewContext();
        var loaded = await db.CounterTransactions.AsNoTracking().Include(t => t.LineItems).Include(t => t.Payments).AsSplitQuery().SingleAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.Paid, loaded.Status);
        var line = Assert.Single(loaded.LineItems);
        Assert.Equal(Pkg001.Value, line.ItemCode);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(new Money(2.95m), line.UnitPrice);
        Assert.Equal(PaymentMethod.Card, Assert.Single(loaded.Payments).Method);
        Assert.Equal(loaded.TotalAmount, loaded.Payments.Single().Amount);
        Assert.Equal(before - 2, await StockAsync(harness, Pkg001));
    }

    [Fact]
    public async Task Failed_payment_rolls_back_every_change()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var clerk = ServiceHarness.Clerk();
        var tx = await harness.Scoped<ITransactionService>().OpenAsync(clerk, 1, CancellationToken.None);
        await harness.Scoped<ITransactionService>().AddRetailItemAsync(tx.Id, new AddRetailItemCommand { Sku = "PKG-001", Quantity = 2 }, clerk, CancellationToken.None);

        // Someone else sells the stock between basket and payment.
        await using (var other = harness.NewContext())
        {
            await other.BranchStock.Where(s => s.BranchCode == Br001 && s.Sku == Pkg001).ExecuteUpdateAsync(s => s.SetProperty(b => b.QuantityOnHand, 1));
        }

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            harness.Scoped<ITransactionService>().PayAsync(tx.Id, PaymentMethod.Cash, clerk, CancellationToken.None));

        await using var db = harness.NewContext();
        var reloaded = await db.CounterTransactions.AsNoTracking().Include(t => t.Payments).SingleAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.Open, reloaded.Status);
        Assert.Empty(reloaded.Payments);
        Assert.Equal(1, await StockAsync(harness, Pkg001));
    }

    [Fact]
    public async Task Concurrent_stock_updates_raise_a_concurrency_conflict_from_the_row_version()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        using var first = harness.NewScope();
        using var second = harness.NewScope();
        var rowA = (await first.ServiceProvider.GetRequiredService<IStockRepository>().GetForUpdateAsync(Br001, [Pkg001], CancellationToken.None)).Single();
        var rowB = (await second.ServiceProvider.GetRequiredService<IStockRepository>().GetForUpdateAsync(Br001, [Pkg001], CancellationToken.None)).Single();

        rowA.Remove(1, harness.Clock.GetUtcNow());
        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        rowB.Remove(1, harness.Clock.GetUtcNow());

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Parallel_clerks_never_oversell_thanks_to_retry_on_conflict()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        await using (var db = harness.NewContext())
        {
            await db.BranchStock.Where(s => s.BranchCode == Br001 && s.Sku == Pkg001).ExecuteUpdateAsync(s => s.SetProperty(b => b.QuantityOnHand, 5));
        }

        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            try
            {
                await harness.Scoped<IInventoryService>().DeductAsync(Br001, [new StockQuantity(Pkg001, 1)], CancellationToken.None);
                return true;
            }
            catch (BusinessRuleViolationException)
            {
                return false;
            }
        });
        var results = await Task.WhenAll(attempts);

        Assert.Equal(5, results.Count(ok => ok));
        Assert.Equal(0, await StockAsync(harness, Pkg001));
    }

    [Fact]
    public async Task Check_constraint_rejects_negative_stock_at_the_database()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        await using var db = harness.NewContext();

        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE dbo.BranchStock SET QuantityOnHand = {-1} WHERE BranchCode = {Br001.Value} AND Sku = {Pkg001.Value}"));

        Assert.Equal(547, ex.Number);
    }

    [Fact]
    public async Task Identity_document_is_stored_masked()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var clerk = ServiceHarness.Clerk();
        var tx = await harness.Scoped<ITransactionService>().OpenAsync(clerk, 1, CancellationToken.None);

        await harness.Scoped<ITransactionService>().AddIdentityVerificationAsync(tx.Id, new() { CustomerName = "Ada Fictional", DocumentType = DocumentType.NationalId, DocumentNumber = "X99887766", Result = VerificationResult.Passed }, clerk, CancellationToken.None);

        await using var db = harness.NewContext();
        var record = await db.IdentityVerifications.AsNoTracking().SingleAsync();
        Assert.Equal("7766", record.DocumentLast4);
        var everything = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.IdentityVerifications WHERE CustomerName LIKE {"%X998877%"} OR DocumentLast4 LIKE {"%X998877%"}").SingleAsync();
        Assert.Equal(0, everything);
        Assert.DoesNotContain(await db.AuditEntries.AsNoTracking().Select(a => a.FieldsJson + a.RouteValuesJson).ToListAsync(), d => d.Contains("X99887766", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stored_procedures_report_daily_sales_and_low_stock()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var clerk = ServiceHarness.Clerk();
        var tx = await harness.Scoped<ITransactionService>().OpenAsync(clerk, 1, CancellationToken.None);
        await harness.Scoped<ITransactionService>().AddRetailItemAsync(tx.Id, new AddRetailItemCommand { Sku = "PKG-001", Quantity = 3 }, clerk, CancellationToken.None);
        await harness.Scoped<ITransactionService>().PayAsync(tx.Id, PaymentMethod.Cash, clerk, CancellationToken.None);

        var reports = harness.Scoped<IReportQueries>();
        var daily = await reports.GetDailySalesAsync(DateOnly.FromDateTime(harness.Clock.Now.UtcDateTime), Br001, CancellationToken.None);
        var low = await reports.GetLowStockAsync(Br001, 1000, CancellationToken.None);

        var row = Assert.Single(daily);
        Assert.Equal(1, row.TransactionCount);
        Assert.Equal(new Money(8.85m), row.RetailNet);
        Assert.NotEmpty(low);
        Assert.All(low, r => Assert.Equal(Br001, r.BranchCode));
    }

    [Fact]
    public async Task Adjust_stock_procedure_updates_stock_writes_history_and_maps_its_errors()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var procedure = harness.Scoped<IStockAdjustmentProcedure>();
        var before = await StockAsync(harness, Pkg001);

        var after = await procedure.AdjustAsync(Br001, Pkg001, 5, "Delivery received", "tester", CancellationToken.None);

        Assert.Equal(before + 5, after);
        await using (var db = harness.NewContext())
        {
            Assert.Equal(1, await db.StockAdjustments.CountAsync());
        }

        var tooMany = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => procedure.AdjustAsync(Br001, Pkg001, -(after + 1), "Write-off", "tester", CancellationToken.None));
        Assert.Equal("STOCK_INSUFFICIENT", tooMany.RuleCode);
        await Assert.ThrowsAsync<CommandValidationException>(() => procedure.AdjustAsync(Br001, new Sku("ZZZ-999"), 1, "Unknown", "tester", CancellationToken.None));
        Assert.Equal(before + 5, await StockAsync(harness, Pkg001));
    }

    [Fact]
    public async Task Racing_pushes_of_the_same_offline_sale_create_one_transaction()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var command = new RecordOfflineSaleCommand
        {
            DeviceId = "TERM-INT-01",
            LocalTransactionId = Guid.NewGuid(),
            BranchCode = "BR-001",
            CounterNumber = 1,
            ClerkStaffId = 7,
            CapturedAt = harness.Clock.Now.AddHours(-1),
            PaymentMethod = PaymentMethod.Cash,
            Lines = [new OfflineSaleLine { Sku = "PKG-001", Quantity = 1, UnitPrice = 2.95m }],
        };

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => harness.Scoped<IOfflineSaleService>().RecordAsync(command, CancellationToken.None)));

        Assert.Single(results.Select(r => r.ServerTransactionId).Distinct());
        Assert.Single(results, r => !r.WasDuplicate);
        await using var db = harness.NewContext();
        Assert.Equal(1, await db.SyncInbox.CountAsync());
        Assert.Equal(1, await db.CounterTransactions.CountAsync(t => t.Source == TransactionSource.OfflineTerminal));
    }

    [Fact]
    public async Task Read_queries_use_a_fixed_number_of_commands_no_n_plus_one()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var clerk = ServiceHarness.Clerk();
        var small = await PaidSaleAsync(harness, clerk, ("PKG-001", 1));
        var large = await PaidSaleAsync(harness, clerk, ("PKG-001", 1), ("PKG-002", 1), ("PKG-003", 1), ("PKG-004", 1), ("PKG-005", 1), ("PKG-006", 1));
        for (var i = 0; i < 3; i++)
        {
            await PaidSaleAsync(harness, clerk, ("PKG-002", 1), ("PKG-003", 1));
        }

        var recorder = new CommandRecorder();
        await using var db = harness.Database.NewContext(recorder);
        var queries = new TransactionQueries(db);

        // The list is one projected query, however many transactions and lines there are.
        var recent = await queries.ListRecentAsync(Br001, 50, CancellationToken.None);
        Assert.Equal(5, recent.Count);
        Assert.Single(recorder.Commands);

        // The details view is a FIXED set of queries: 3 split queries for the aggregate (header,
        // lines, payments) + shipments + money orders + verifications. It must not grow with the
        // number of lines (that would be N+1).
        recorder.Clear();
        Assert.NotNull(await queries.GetDetailsAsync(small, CancellationToken.None));
        var smallCount = recorder.Commands.Count;

        recorder.Clear();
        var details = await queries.GetDetailsAsync(large, CancellationToken.None);
        Assert.NotNull(details);
        Assert.Equal(6, details.Lines.Count);
        Assert.Equal(smallCount, recorder.Commands.Count);
        Assert.InRange(recorder.Commands.Count, 1, 6);
        foreach (var sql in recorder.Commands)
        {
            output.WriteLine(sql);
        }
    }

    private static async Task<int> PaidSaleAsync(ServiceHarness harness, ApexPost.Services.Common.StaffContext clerk, params (string Sku, int Quantity)[] items)
    {
        var tx = await harness.Scoped<ITransactionService>().OpenAsync(clerk, 1, CancellationToken.None);
        foreach (var (sku, quantity) in items)
        {
            await harness.Scoped<ITransactionService>().AddRetailItemAsync(tx.Id, new AddRetailItemCommand { Sku = sku, Quantity = quantity }, clerk, CancellationToken.None);
        }

        await harness.Scoped<ITransactionService>().PayAsync(tx.Id, PaymentMethod.Cash, clerk, CancellationToken.None);
        return tx.Id;
    }

    [Fact]
    public async Task Recent_transactions_query_is_parameterized_sql()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        await using var db = harness.NewContext();

        var sql = new TransactionQueries(db).RecentQuery(Br001, 20).ToQueryString();
        output.WriteLine(sql);

        // ToQueryString prints each parameter as a DECLARE with its value for readability; the
        // statement itself must reference the parameters, never inline the values.
        var select = sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
        Assert.Contains("DECLARE @", sql, StringComparison.Ordinal);
        Assert.Contains("@", select, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", select, StringComparison.Ordinal);
        Assert.DoesNotContain("'BR-001'", select, StringComparison.Ordinal);
        var folder = Path.Combine(AppContext.BaseDirectory, "sql-evidence");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "RecentTransactions.sql"), sql);
    }

    private static async Task<int> StockAsync(ServiceHarness harness, Sku sku)
    {
        await using var db = harness.NewContext();
        return await db.BranchStock.AsNoTracking().Where(s => s.BranchCode == Br001 && s.Sku == sku).Select(s => s.QuantityOnHand).SingleAsync();
    }
}
