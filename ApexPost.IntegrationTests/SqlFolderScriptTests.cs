using System.Data;
using System.Text.RegularExpressions;
using ApexPost.Tests.Support;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace ApexPost.IntegrationTests;

/// <summary>
/// Runs the standalone scripts in /sql (Week 3 Part B) against a fresh, migrated database, split
/// into batches on GO exactly as SSMS and sqlcmd do, on one connection (script 05 uses a temporary
/// procedure that lives for the connection). The blocking demo (06-08) is interactive and is not
/// run here.
/// </summary>
public sealed partial class SqlFolderScriptTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("01-daily-sales-by-branch.sql")]
    [InlineData("02-top-products-by-revenue-per-region.sql")]
    [InlineData("03-shipping-volume-by-zone.sql")]
    [InlineData("04-money-order-audit.sql")]
    [InlineData("05-sale-transaction.sql")]
    public async Task Script_runs_without_errors(string script)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var results = await RunScriptAsync(database, script);

        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task Daily_sales_lists_every_branch_even_without_sales()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var branches = await ScalarAsync(database, "SELECT COUNT(*) FROM dbo.Branches");

        var report = (await RunScriptAsync(database, "01-daily-sales-by-branch.sql"))[0];

        Assert.Equal(branches, report.Rows.Count);
        Assert.All(report.Rows.Cast<DataRow>(), r => Assert.True((int)r["PaidTransactions"] >= 0));
    }

    [Fact]
    public async Task Top_products_never_returns_more_than_ten_per_region()
    {
        await using var database = await SqlTestDatabase.CreateAsync();

        // Commit one real sale (12 different SKUs at BR-001) so there is revenue to rank.
        var skus = await SkusInStockAsync(database, "BR-001", 12);
        var items = string.Join(",", skus.Select(s => "{\"sku\":\"" + s + "\",\"qty\":1}"));
        var script = (await File.ReadAllTextAsync(Path.Combine(SqlFolder(), "05-sale-transaction.sql")))
            .Replace("@Commit        = 0,", "@Commit        = 1,", StringComparison.Ordinal)
            .Replace("N'[{\"sku\":\"PKG-001\",\"qty\":2},{\"sku\":\"PKG-002\",\"qty\":1}]'", "N'[" + items + "]'", StringComparison.Ordinal);
        await RunTextAsync(database, script, "05 (committed)");
        Assert.Equal(1, await ScalarAsync(database, "SELECT COUNT(*) FROM dbo.CounterTransactions WHERE CreatedBy = 'sql-script' AND Status = 'Paid'"));

        var report = (await RunScriptAsync(database, "02-top-products-by-revenue-per-region.sql"))[0];

        var regions = report.Rows.Cast<DataRow>().GroupBy(r => (string)r["Region"]).ToList();
        Assert.NotEmpty(regions);
        Assert.All(regions, region => Assert.InRange(region.Count(), 1, 10));
        Assert.All(regions, region => Assert.Equal(Enumerable.Range(1, region.Count()).Select(i => (long)i), region.Select(r => (long)r["Position"])));
    }

    [Fact]
    public async Task Sale_script_is_a_dry_run_that_leaves_no_trace()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        const string stock = "SELECT SUM(QuantityOnHand) FROM dbo.BranchStock WHERE BranchCode = 'BR-001'";
        var stockBefore = await ScalarAsync(database, stock);
        var salesBefore = await ScalarAsync(database, "SELECT COUNT(*) FROM dbo.CounterTransactions");

        var results = await RunScriptAsync(database, "05-sale-transaction.sql");

        Assert.Equal(stockBefore, await ScalarAsync(database, stock));
        Assert.Equal(salesBefore, await ScalarAsync(database, "SELECT COUNT(*) FROM dbo.CounterTransactions"));
        Assert.Equal(0, await ScalarAsync(database, "SELECT COUNT(*) FROM dbo.Payments WHERE CreatedBy = 'sql-script'"));

        // The failure scenarios each report their error, with no transaction left open.
        var failures = results.Where(t => t.Columns.Contains("Scenario")).Select(t => t.Rows[0]).ToList();
        Assert.Equal(3, failures.Count);
        Assert.Equal(51003, (int)failures[0]["ErrorNumber"]);  // not enough stock
        Assert.Equal(547, (int)failures[1]["ErrorNumber"]);    // CHECK constraint (payment method)
        Assert.Equal(51000, (int)failures[2]["ErrorNumber"]);  // empty basket
        Assert.All(failures, f => Assert.Equal(0, (int)f["OpenTransactions"]));
    }

    private async Task<List<DataTable>> RunScriptAsync(SqlTestDatabase database, string script) =>
        await RunTextAsync(database, await File.ReadAllTextAsync(Path.Combine(SqlFolder(), script)), script);

    private async Task<List<DataTable>> RunTextAsync(SqlTestDatabase database, string text, string script)
    {
        var tables = new List<DataTable>();

        await using var connection = new SqlConnection(database.ConnectionString);
        connection.InfoMessage += (_, e) => output.WriteLine(e.Message);
        await connection.OpenAsync();

        foreach (var batch in GoSeparator().Split(text).Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
            await using var reader = await command.ExecuteReaderAsync();
            do
            {
                if (reader.FieldCount == 0)
                {
                    continue; // a statement without a result set (DDL, SET, PRINT)
                }

                var table = new DataTable();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    table.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
                }

                while (await reader.ReadAsync())
                {
                    var values = new object[reader.FieldCount];
                    reader.GetValues(values);
                    table.Rows.Add(values);
                }

                tables.Add(table);
            }
            while (await reader.NextResultAsync());
        }

        output.WriteLine($"{script}: {tables.Count} result set(s)");
        return tables;
    }

    private static async Task<List<string>> SkusInStockAsync(SqlTestDatabase database, string branch, int count)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT TOP (@count) s.Sku FROM dbo.BranchStock AS s JOIN dbo.Products AS p ON p.Sku = s.Sku WHERE s.BranchCode = @branch AND s.QuantityOnHand > 0 AND p.IsActive = 1 ORDER BY s.Sku",
            connection);
        command.Parameters.Add(new SqlParameter("@count", SqlDbType.Int) { Value = count });
        command.Parameters.Add(new SqlParameter("@branch", SqlDbType.VarChar, 10) { Value = branch });
        var skus = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            skus.Add(reader.GetString(0));
        }

        return skus;
    }

    private static async Task<int> ScalarAsync(SqlTestDatabase database, string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The repository's /sql folder, found by walking up from the test binaries.</summary>
    private static string SqlFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ApexPost.slnx")))
            {
                return Path.Combine(dir.FullName, "sql");
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root (ApexPost.slnx) above " + AppContext.BaseDirectory);
    }

    // "GO" alone on a line (any case, optional surrounding spaces) separates batches, as in SSMS.
    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex GoSeparator();
}
