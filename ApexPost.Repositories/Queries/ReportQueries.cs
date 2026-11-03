using ApexPost.Database;
using ApexPost.Database.Configurations;
using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Repositories.Persistence;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Reports;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Repositories.Queries;

/// <summary>
/// Report queries. Aggregations run in SQL Server (GROUP BY / SUM), never by loading rows into
/// memory. Where the shape is a fixed report, the SQL is either a stored procedure
/// (usp_DailyBranchSales, usp_LowStockReport) or a parameterized SqlQuery: EF Core turns every
/// interpolated value into a SqlParameter, so nothing is string-concatenated.
/// </summary>
public sealed class ReportQueries(ApexPostDbContext db) : IReportQueries
{
    public async Task<DateOnly?> GetLatestTradingDayAsync(DateOnly today, CancellationToken cancellationToken) =>
        await db.CounterTransactions.AsNoTracking()
            .Where(t => t.Status == TransactionStatus.Paid)
            .Select(t => EF.Property<DateOnly?>(t, "BusinessDate"))
            .Where(d => d != null && d <= today)
            .MaxAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<DailySalesRow>> GetDailySalesAsync(DateOnly date, BranchCode? branch, CancellationToken cancellationToken)
    {
        var branchCode = branch?.Value;

        // BusinessDate is a persisted computed column with an index (BranchCode, BusinessDate):
        // "BusinessDate = @date" is SARGable, unlike CONVERT(date, ClosedAtUtc) = @date.
        var rows = await db.Database.SqlQuery<DailySalesSqlRow>($"""
            SELECT b.BranchCode,
                   b.Name AS BranchName,
                   COUNT(CASE WHEN t.Status = 'Paid' THEN 1 END)                       AS TransactionCount,
                   COALESCE(SUM(CASE WHEN t.Status = 'Paid' THEN r.RetailNet END), 0)   AS RetailNet,
                   COALESCE(SUM(CASE WHEN t.Status = 'Paid' THEN r.ServicesNet END), 0) AS ServicesNet,
                   COALESCE(SUM(CASE WHEN t.Status = 'Paid' THEN t.TaxAmount END), 0)   AS Tax,
                   COALESCE(SUM(CASE WHEN t.Status = 'Paid' THEN t.PassThroughAmount END), 0) AS PassThrough,
                   COALESCE(SUM(CASE WHEN t.Status = 'Paid' THEN t.TotalAmount END), 0) AS Total,
                   COUNT(CASE WHEN t.Status = 'Voided' THEN 1 END)                      AS VoidedCount,
                   COUNT(CASE WHEN t.Status = 'Refunded' THEN 1 END)                    AS RefundedCount
            FROM dbo.Branches AS b
            LEFT JOIN dbo.CounterTransactions AS t
                   ON t.BranchCode = b.BranchCode AND t.BusinessDate = {date}
            OUTER APPLY (SELECT SUM(CASE WHEN l.LineType = 'RetailItem' THEN l.LineTotal ELSE 0 END)  AS RetailNet,
                                SUM(CASE WHEN l.LineType <> 'RetailItem' THEN l.LineTotal ELSE 0 END) AS ServicesNet
                         FROM dbo.TransactionLines AS l
                         WHERE l.TransactionId = t.Id) AS r
            WHERE ({branchCode} IS NULL OR b.BranchCode = {branchCode})
            GROUP BY b.BranchCode, b.Name
            ORDER BY b.BranchCode
            """).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.Select(r => new DailySalesRow(
            new BranchCode(r.BranchCode), r.BranchName, r.TransactionCount, new Money(r.RetailNet), new Money(r.ServicesNet),
            new Money(r.Tax), new Money(r.PassThrough), new Money(r.Total), r.VoidedCount, r.RefundedCount)).ToList();
    }

    public async Task<BranchSalesSplit> GetBranchSalesSplitAsync(BranchCode branch, DateOnly date, CancellationToken cancellationToken)
    {
        var rows = await db.Database
            .SqlQuery<BranchSalesSqlRow>($"EXEC dbo.usp_DailyBranchSales @BranchCode = {branch.Value}, @Date = {date}")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var r = rows.Single();
        return new BranchSalesSplit(branch, date, r.PaidTransactions, new Money(r.RetailTotal), new Money(r.ServicesTotal), new Money(r.TaxTotal), new Money(r.GrandTotal));
    }

    public async Task<IReadOnlyList<LowStockRow>> GetLowStockAsync(BranchCode? branch, int threshold, CancellationToken cancellationToken)
    {
        var branchCode = branch?.Value;
        var rows = await db.Database
            .SqlQuery<LowStockSqlRow>($"EXEC dbo.usp_LowStockReport @BranchCode = {branchCode}, @Threshold = {threshold}")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new LowStockRow(new BranchCode(r.BranchCode), new Sku(r.Sku), r.ProductName, (ProductCategory)r.CategoryId, r.QuantityOnHand, r.ReorderThreshold)).ToList();
    }

    public async Task<IReadOnlyList<ShippingVolumeRow>> GetShippingVolumeAsync(DateOnly from, DateOnly to, BranchCode? branch, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var branchCode = branch?.Value;
        var rows = await db.Database.SqlQuery<ShippingVolumeSqlRow>($"""
            SELECT s.ZoneCode, z.Name AS ZoneName, s.ServiceLevel,
                   COUNT(*) AS ShipmentCount, SUM(s.WeightKg) AS TotalWeightKg, SUM(s.Price) AS Revenue
            FROM dbo.Shipments AS s
            JOIN dbo.ShippingZones AS z ON z.ZoneCode = s.ZoneCode
            WHERE s.Status = 'Lodged'
              AND s.QuotedAtUtc >= {start} AND s.QuotedAtUtc < {end}
              AND ({branchCode} IS NULL OR s.BranchCode = {branchCode})
            GROUP BY s.ZoneCode, z.Name, s.ServiceLevel
            ORDER BY s.ZoneCode, s.ServiceLevel
            """).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.Select(r => new ShippingVolumeRow(r.ZoneCode, r.ZoneName, Enum.Parse<ShippingServiceLevel>(r.ServiceLevel), r.ShipmentCount, new Weight(r.TotalWeightKg), new Money(r.Revenue))).ToList();
    }

    public async Task<MoneyOrderAuditData> GetMoneyOrderAuditAsync(DateOnly from, DateOnly to, BranchCode? branch, decimal highValueThreshold, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var branchCode = branch?.Value;
        var rows = await db.Database.SqlQuery<MoneyOrderAuditSqlRow>($"""
            SELECT b.BranchCode, b.Name AS BranchName,
                   COALESCE(mo.MoneyOrderCount, 0) AS MoneyOrderCount,
                   COALESCE(mo.FaceValue, 0)       AS FaceValue,
                   COALESCE(mo.Fees, 0)            AS Fees,
                   COALESCE(mo.HighValueCount, 0)  AS HighValueCount,
                   COALESCE(iv.Passed, 0)          AS VerificationsPassed,
                   COALESCE(iv.Failed, 0)          AS VerificationsFailed
            FROM dbo.Branches AS b
            LEFT JOIN (SELECT BranchCode, COUNT(*) AS MoneyOrderCount, SUM(Amount) AS FaceValue, SUM(Fee) AS Fees,
                              SUM(CASE WHEN Amount >= {highValueThreshold} THEN 1 ELSE 0 END) AS HighValueCount
                       FROM dbo.MoneyOrders
                       WHERE Status = 'Issued' AND RequestedAtUtc >= {start} AND RequestedAtUtc < {end}
                       GROUP BY BranchCode) AS mo ON mo.BranchCode = b.BranchCode
            LEFT JOIN (SELECT BranchCode,
                              SUM(CASE WHEN ResultCode = 'PASS' THEN 1 ELSE 0 END) AS Passed,
                              SUM(CASE WHEN ResultCode = 'FAIL' THEN 1 ELSE 0 END) AS Failed
                       FROM dbo.IdentityVerifications
                       WHERE VerifiedAtUtc >= {start} AND VerifiedAtUtc < {end}
                       GROUP BY BranchCode) AS iv ON iv.BranchCode = b.BranchCode
            WHERE ({branchCode} IS NULL OR b.BranchCode = {branchCode})
            ORDER BY b.BranchCode
            """).ToListAsync(cancellationToken).ConfigureAwait(false);

        var orders = await MoneyOrderAuditListQuery(from, to, branch, take: 500)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var checks = await db.IdentityVerifications.AsNoTracking()
            .Where(v => v.VerifiedAtUtc >= start && v.VerifiedAtUtc < end && (branch == null || v.BranchCode == branch))
            .OrderByDescending(v => v.VerifiedAtUtc)
            .Take(500)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new MoneyOrderAuditData(
            rows.Select(r => new MoneyOrderAuditRow(new BranchCode(r.BranchCode), r.BranchName, r.MoneyOrderCount, new Money(r.FaceValue), new Money(r.Fees), r.HighValueCount, r.VerificationsPassed, r.VerificationsFailed)).ToList(),
            orders,
            checks.Select(IdentityVerificationMapper.ToDto).ToList());
    }

    /// <summary>
    /// The money-order list of the audit report: non-pending orders in a date range, newest first,
    /// capped, projected to the DTO in SQL. The optional branch is a parameter
    /// (<c>@branch IS NULL OR BranchCode = @branch</c>), not a second query shape. Exposed so tests
    /// can capture its SQL with ToQueryString.
    /// </summary>
    public IQueryable<MoneyOrderDto> MoneyOrderAuditListQuery(DateOnly from, DateOnly to, BranchCode? branch, int take)
    {
        var (start, end) = Range(from, to);
        var startOffset = new DateTimeOffset(start, TimeSpan.Zero);
        var endOffset = new DateTimeOffset(end, TimeSpan.Zero);
        return db.MoneyOrders.AsNoTracking()
            .Where(m => m.Status != MoneyOrderStatus.Pending && m.CreatedAt >= startOffset && m.CreatedAt < endOffset && (branch == null || m.BranchCode == branch))
            .OrderByDescending(m => m.CreatedAt)
            .Take(take)
            .Select(m => new MoneyOrderDto(m.Id, m.SerialNumber, m.TransactionId, m.BranchCode, m.Amount, m.Fee, m.Sender, m.Recipient, m.IdentityVerificationId, m.Status, m.CreatedAt, m.IssuedAt));
    }

    /// <summary>Inclusive calendar range → half-open UTC range [from 00:00, to+1 00:00): SARGable on the datetime column.</summary>
    private static (DateTime Start, DateTime End) Range(DateOnly from, DateOnly to) =>
        (from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    private sealed class DailySalesSqlRow
    {
        public string BranchCode { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public int TransactionCount { get; set; }
        public decimal RetailNet { get; set; }
        public decimal ServicesNet { get; set; }
        public decimal Tax { get; set; }
        public decimal PassThrough { get; set; }
        public decimal Total { get; set; }
        public int VoidedCount { get; set; }
        public int RefundedCount { get; set; }
    }

    private sealed class BranchSalesSqlRow
    {
        public string BranchCode { get; set; } = string.Empty;
        public DateOnly BusinessDate { get; set; }
        public int PaidTransactions { get; set; }
        public decimal RetailTotal { get; set; }
        public decimal ServicesTotal { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal GrandTotal { get; set; }
    }

    private sealed class LowStockSqlRow
    {
        public string BranchCode { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public int QuantityOnHand { get; set; }
        public int ReorderThreshold { get; set; }
    }

    private sealed class ShippingVolumeSqlRow
    {
        public string ZoneCode { get; set; } = string.Empty;
        public string ZoneName { get; set; } = string.Empty;
        public string ServiceLevel { get; set; } = string.Empty;
        public int ShipmentCount { get; set; }
        public decimal TotalWeightKg { get; set; }
        public decimal Revenue { get; set; }
    }

    private sealed class MoneyOrderAuditSqlRow
    {
        public string BranchCode { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public int MoneyOrderCount { get; set; }
        public decimal FaceValue { get; set; }
        public decimal Fees { get; set; }
        public int HighValueCount { get; set; }
        public int VerificationsPassed { get; set; }
        public int VerificationsFailed { get; set; }
    }
}

public sealed class SyncQueries(ApexPostDbContext db) : ISyncQueries
{
    public async Task<BranchSnapshot> GetBranchSnapshotAsync(BranchCode branch, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var cutoff = since?.UtcDateTime;
        var generatedAt = DateTimeOffset.UtcNow;

        // Delta pull: rows whose audit columns changed after the terminal's last sync token.
        var products = await db.Products.AsNoTracking()
            .Where(p => cutoff == null
                        || EF.Property<DateTime>(p, "CreatedAtUtc") > cutoff
                        || EF.Property<DateTime?>(p, "ModifiedAtUtc") > cutoff)
            .OrderBy(p => p.Sku)
            .Select(p => new { p.Id, p.Sku, p.Name, p.Category, UnitPrice = EF.Property<decimal>(p, ProductColumns.UnitPrice), p.TaxCategory, p.IsActive })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var stock = await (from s in db.BranchStock.AsNoTracking()
                           join p in db.Products.AsNoTracking() on s.Sku equals p.Sku
                           where s.BranchCode == branch
                                 && (cutoff == null
                                     || EF.Property<DateTime>(s, "CreatedAtUtc") > cutoff
                                     || EF.Property<DateTime?>(s, "ModifiedAtUtc") > cutoff)
                           orderby s.Sku
                           select new StockDto(s.BranchCode, s.Sku, p.Name, s.QuantityOnHand, s.ReorderThreshold))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new BranchSnapshot(
            branch,
            generatedAt,
            products.Select(p => new ProductDto(p.Id, p.Sku, p.Name, p.Category, new Money(p.UnitPrice), p.TaxCategory, p.IsActive)).ToList(),
            stock);
    }

    public async Task<IReadOnlyList<SyncConflictDto>> ListConflictsAsync(BranchCode? branch, int take, CancellationToken cancellationToken) =>
        await (from c in db.SyncConflicts.AsNoTracking()
               join s in db.SyncInbox.AsNoTracking() on c.SyncInboxEntryId equals s.Id
               where branch == null || s.BranchCode == branch
               orderby c.RaisedAt descending
               select new SyncConflictDto(c.Id, s.DeviceId, s.LocalTransactionId, s.BranchCode, c.Type, c.ItemCode, c.Details, c.Status, c.RaisedAt, s.ServerTransactionId))
            .Take(take)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}
