using ApexPost.Database;
using ApexPost.Models.ValueObjects;
using ApexPost.Repositories.Queries;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace ApexPost.IntegrationTests;

/// <summary>
/// ToQueryString evidence for three representative LINQ queries (PERFORMANCE.md §6). ToQueryString
/// only needs the SQL Server provider, not a live server, so these run anywhere. Each test checks
/// the shape of the SQL (projection, filter, ordering, parameters) and writes it to
/// <c>sql-evidence/</c> next to the test binaries; the same text is committed in
/// <c>database/querystring/</c>.
/// </summary>
public sealed class ToQueryStringEvidenceTests(ITestOutputHelper output) : IDisposable
{
    private static readonly BranchCode Br001 = new("BR-001");

    private readonly ApexPostDbContext _db = new(
        new DbContextOptionsBuilder<ApexPostDbContext>().UseSqlServer("Server=not-used;Database=not-used").Options);

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Q1_recent_transactions_is_one_parameterized_projection()
    {
        var sql = Capture("Q1-RecentTransactions.sql", new TransactionQueries(_db).RecentQuery(Br001, 20).ToQueryString());
        var statement = Statement(sql);

        Assert.StartsWith("SELECT TOP(@", statement, StringComparison.Ordinal);
        Assert.Contains("FROM [dbo].[CounterTransactions] AS [c]", statement, StringComparison.Ordinal);
        Assert.Contains("WHERE [c].[BranchCode] = @", statement, StringComparison.Ordinal);
        Assert.Contains("ORDER BY [c].[OpenedAtUtc] DESC", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("'BR-001'", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void Q2_branch_stock_is_one_join_projected_to_the_dto()
    {
        var sql = Capture("Q2-BranchStock.sql", new CatalogQueries(_db).StockForBranchQuery(Br001).ToQueryString());
        var statement = Statement(sql);

        Assert.Contains("FROM [dbo].[BranchStock] AS [b]", statement, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN [dbo].[Products] AS [p] ON [b].[Sku] = [p].[Sku]", statement, StringComparison.Ordinal);
        Assert.Contains("WHERE [b].[BranchCode] = @", statement, StringComparison.Ordinal);
        Assert.Contains("ORDER BY [p].[Name]", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("[p].[UnitPrice]", statement, StringComparison.Ordinal); // only the columns the DTO needs
        Assert.DoesNotContain("'BR-001'", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void Q3_money_order_audit_list_is_a_sargable_parameterized_range()
    {
        var query = new ReportQueries(_db).MoneyOrderAuditListQuery(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Br001, take: 500);
        var sql = Capture("Q3-MoneyOrderAuditList.sql", query.ToQueryString());
        var statement = Statement(sql);

        Assert.StartsWith("SELECT TOP(@", statement, StringComparison.Ordinal);
        Assert.Contains("FROM [dbo].[MoneyOrders] AS [m]", statement, StringComparison.Ordinal);
        Assert.Contains("[m].[RequestedAtUtc] >= @", statement, StringComparison.Ordinal);
        Assert.Contains("[m].[RequestedAtUtc] < @", statement, StringComparison.Ordinal);
        Assert.Contains("ORDER BY [m].[RequestedAtUtc] DESC", statement, StringComparison.Ordinal);
        Assert.Contains("[m].[BranchCode] = @", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-10", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("'BR-001'", statement, StringComparison.Ordinal);
    }

    private string Capture(string fileName, string sql)
    {
        output.WriteLine(sql);
        var folder = Path.Combine(AppContext.BaseDirectory, "sql-evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, fileName), sql);
        return sql;
    }

    // ToQueryString prints each parameter as a DECLARE line (to show its value); the statement that
    // is actually sent starts at the first SELECT and only references the @parameters.
    private static string Statement(string sql) => sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
}
