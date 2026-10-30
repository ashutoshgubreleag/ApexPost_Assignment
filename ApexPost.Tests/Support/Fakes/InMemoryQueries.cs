using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Common;
using ApexPost.Services.Reports;

namespace ApexPost.Tests.Support.Fakes;

/// <summary>
/// Read side over the in-memory store, mirroring the EF Core query services' results (the SQL
/// versions are covered by the SQL Server integration tests). Used by unit tests and by the
/// WebApplicationFactory tests that exercise the pipeline without a database.
/// </summary>
public sealed class InMemoryQueries(InMemoryStore store) :
    IBranchQueries, IStaffQueries, ICatalogQueries, ITransactionQueries, IReportQueries, INotificationQueries, ISyncQueries
{
    public Task<IReadOnlyList<BranchDto>> ListAsync(CancellationToken cancellationToken) =>
        Done<BranchDto>(store.All<Branch>().OrderBy(b => b.Code.Value, StringComparer.Ordinal).Select(b => b.ToDto()));

    public Task<BranchDto?> GetAsync(BranchCode code, CancellationToken cancellationToken) =>
        Task.FromResult(store.All<Branch>().Where(b => b.Code == code).Select(b => b.ToDto()).SingleOrDefault());

    public Task<IReadOnlyList<StaffDto>> ListAsync(BranchCode? branch, CancellationToken cancellationToken) =>
        Done<StaffDto>(store.All<StaffMember>().Where(s => branch is null || s.HomeBranchCode == branch)
            .OrderBy(s => s.HomeBranchCode.Value, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.Ordinal).Select(s => s.ToDto()));

    public Task<StaffDto?> GetAsync(int staffId, CancellationToken cancellationToken) =>
        Task.FromResult(store.All<StaffMember>().Where(s => s.Id == staffId).Select(s => s.ToDto()).SingleOrDefault());

    public Task<IReadOnlyList<ProductDto>> ListProductsAsync(CancellationToken cancellationToken) =>
        Done<ProductDto>(store.All<Product>().OrderBy(p => p.Sku.Value, StringComparer.Ordinal).Select(p => p.ToDto()));

    public Task<IReadOnlyList<ZoneDto>> ListZonesAsync(CancellationToken cancellationToken) =>
        Done<ZoneDto>(store.All<ShippingZone>().OrderBy(z => z.ZoneCode, StringComparer.Ordinal)
            .Select(z => new ZoneDto(z.ZoneCode, z.Name, z.IsInternational, z.IsRemote, z.LetterBaseRate, z.ParcelBaseRate, z.PerKgRate)));

    public Task<IReadOnlyList<StockDto>> GetStockForBranchAsync(BranchCode branch, CancellationToken cancellationToken) =>
        Task.FromResult(StockFor(branch));

    public Task<IReadOnlyList<TransactionSummaryDto>> ListRecentAsync(BranchCode? branch, int take, CancellationToken cancellationToken) =>
        Done<TransactionSummaryDto>(store.All<CounterTransaction>().Where(t => branch is null || t.BranchCode == branch)
            .OrderByDescending(t => t.CreatedAt).Take(take).Select(t => t.ToSummary()));

    public Task<TransactionSummaryDto?> GetSummaryAsync(int transactionId, CancellationToken cancellationToken) =>
        Task.FromResult(store.All<CounterTransaction>().Where(t => t.Id == transactionId).Select(t => t.ToSummary()).SingleOrDefault());

    public Task<TransactionDetailsData?> GetDetailsAsync(int transactionId, CancellationToken cancellationToken)
    {
        var tx = store.All<CounterTransaction>().SingleOrDefault(t => t.Id == transactionId);
        return Task.FromResult(tx is null ? null : new TransactionDetailsData(
            tx.ToSummary(),
            tx.LineItems.OrderBy(l => l.LineNumber).Select(l => new TransactionLineDto(l.LineNumber, l.LineType, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.TaxCategory, l.TaxAmount, l.LineTotal, l.PassThroughAmount)).ToList(),
            tx.Payments.Select(p => new PaymentDto(p.Method, p.Amount, p.PaidAt)).ToList(),
            store.All<Shipment>().Where(s => s.TransactionId == transactionId).OrderBy(s => s.Id).Select(s => s.ToDto()).ToList(),
            store.All<MoneyOrder>().Where(m => m.TransactionId == transactionId).OrderBy(m => m.Id).Select(m => m.ToDto()).ToList(),
            store.All<IdentityVerification>().Where(v => v.TransactionId == transactionId).OrderBy(v => v.Id).Select(v => v.ToDto()).ToList()));
    }

    public Task<ShipmentDto?> GetShipmentAsync(int shipmentId, CancellationToken cancellationToken) =>
        Task.FromResult(store.All<Shipment>().Where(s => s.Id == shipmentId).Select(s => s.ToDto()).SingleOrDefault());

    public Task<ShipmentDto?> GetShipmentByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken) =>
        Task.FromResult(store.All<Shipment>().Where(s => string.Equals(s.TrackingNumber, trackingNumber, StringComparison.OrdinalIgnoreCase)).Select(s => s.ToDto()).SingleOrDefault());

    public Task<MoneyOrderDto?> GetMoneyOrderAsync(int moneyOrderId, CancellationToken cancellationToken) =>
        Task.FromResult(store.All<MoneyOrder>().Where(m => m.Id == moneyOrderId).Select(m => m.ToDto()).SingleOrDefault());

    public Task<IReadOnlyList<MoneyOrderDto>> ListMoneyOrdersAsync(BranchCode? branch, int take, CancellationToken cancellationToken) =>
        Done<MoneyOrderDto>(store.All<MoneyOrder>().Where(m => branch is null || m.BranchCode == branch).OrderByDescending(m => m.CreatedAt).Take(take).Select(m => m.ToDto()));

    public Task<DateOnly?> GetLatestTradingDayAsync(DateOnly today, CancellationToken cancellationToken) =>
        Task.FromResult(store.All<CounterTransaction>().Where(t => t.Status == TransactionStatus.Paid && t.ClosedAt is not null)
            .Select(t => (DateOnly?)DateOnly.FromDateTime(t.ClosedAt!.Value.UtcDateTime)).Where(d => d <= today).DefaultIfEmpty(null).Max());

    public Task<IReadOnlyList<DailySalesRow>> GetDailySalesAsync(DateOnly date, BranchCode? branch, CancellationToken cancellationToken)
    {
        var day = store.All<CounterTransaction>().Where(t => t.ClosedAt is { } c && DateOnly.FromDateTime(c.UtcDateTime) == date).ToList();
        return Done<DailySalesRow>(store.All<Branch>().Where(b => branch is null || b.Code == branch).OrderBy(b => b.Code.Value, StringComparer.Ordinal).Select(b =>
        {
            var at = day.Where(t => t.BranchCode == b.Code).ToList();
            var paid = at.Where(t => t.Status == TransactionStatus.Paid).ToList();
            return new DailySalesRow(b.Code, b.Name, paid.Count, paid.Select(t => t.RetailNet).Total(), paid.Select(t => t.ServicesNet).Total(),
                paid.Select(t => t.TaxAmount).Total(), paid.Select(t => t.PassThroughAmount).Total(), paid.Select(t => t.TotalAmount).Total(),
                at.Count(t => t.Status == TransactionStatus.Voided), at.Count(t => t.Status == TransactionStatus.Refunded));
        }));
    }

    public Task<BranchSalesSplit> GetBranchSalesSplitAsync(BranchCode branch, DateOnly date, CancellationToken cancellationToken)
    {
        var paid = store.All<CounterTransaction>().Where(t => t.BranchCode == branch && t.Status == TransactionStatus.Paid && t.ClosedAt is { } c && DateOnly.FromDateTime(c.UtcDateTime) == date).ToList();
        return Task.FromResult(new BranchSalesSplit(branch, date, paid.Count, paid.Select(t => t.RetailNet).Total(), paid.Select(t => t.ServicesNet).Total(), paid.Select(t => t.TaxAmount).Total(), paid.Select(t => t.TotalAmount).Total()));
    }

    public Task<IReadOnlyList<LowStockRow>> GetLowStockAsync(BranchCode? branch, int threshold, CancellationToken cancellationToken)
    {
        var products = store.All<Product>().ToDictionary(p => p.Sku);
        return Done<LowStockRow>(store.All<BranchStock>()
            .Where(s => (branch is null || s.BranchCode == branch) && s.QuantityOnHand <= Math.Max(s.ReorderThreshold, threshold))
            .OrderBy(s => s.BranchCode.Value, StringComparer.Ordinal).ThenBy(s => s.QuantityOnHand).ThenBy(s => s.Sku.Value, StringComparer.Ordinal)
            .Select(s => new LowStockRow(s.BranchCode, s.Sku, products[s.Sku].Name, products[s.Sku].Category, s.QuantityOnHand, s.ReorderThreshold)));
    }

    public Task<IReadOnlyList<ShippingVolumeRow>> GetShippingVolumeAsync(DateOnly from, DateOnly to, BranchCode? branch, CancellationToken cancellationToken)
    {
        var zones = store.All<ShippingZone>().ToDictionary(z => z.ZoneCode);
        return Done<ShippingVolumeRow>(store.All<Shipment>()
            .Where(s => s.Status == ShipmentStatus.Lodged && InRange(s.CreatedAt, from, to) && (branch is null || s.BranchCode == branch))
            .GroupBy(s => (s.ZoneCode, s.ServiceLevel))
            .Select(g => new ShippingVolumeRow(g.Key.ZoneCode, zones[g.Key.ZoneCode].Name, g.Key.ServiceLevel, g.Count(), new Weight(g.Sum(s => s.Weight.Kilograms)), g.Select(s => s.Price).Total()))
            .OrderBy(r => r.ZoneCode, StringComparer.Ordinal).ThenBy(r => r.ServiceLevel));
    }

    public Task<MoneyOrderAuditData> GetMoneyOrderAuditAsync(DateOnly from, DateOnly to, BranchCode? branch, decimal highValueThreshold, CancellationToken cancellationToken)
    {
        var orders = store.All<MoneyOrder>().Where(m => m.Status != MoneyOrderStatus.Pending && InRange(m.CreatedAt, from, to) && (branch is null || m.BranchCode == branch)).ToList();
        var checks = store.All<IdentityVerification>().Where(v => InRange(v.VerifiedAt, from, to) && (branch is null || v.BranchCode == branch)).ToList();
        var rows = store.All<Branch>().Where(b => branch is null || b.Code == branch).OrderBy(b => b.Code.Value, StringComparer.Ordinal).Select(b =>
        {
            var issued = orders.Where(o => o.BranchCode == b.Code && o.Status == MoneyOrderStatus.Issued).ToList();
            var bc = checks.Where(v => v.BranchCode == b.Code).ToList();
            return new MoneyOrderAuditRow(b.Code, b.Name, issued.Count, issued.Select(o => o.Amount).Total(), issued.Select(o => o.Fee).Total(),
                issued.Count(o => o.Amount.Amount >= highValueThreshold), bc.Count(v => v.Result == VerificationResult.Passed), bc.Count(v => v.Result == VerificationResult.Failed));
        }).ToList();
        return Task.FromResult(new MoneyOrderAuditData(rows,
            orders.OrderByDescending(o => o.CreatedAt).Select(o => o.ToDto()).ToList(),
            checks.OrderByDescending(v => v.VerifiedAt).Select(v => v.ToDto()).ToList()));
    }

    Task<IReadOnlyList<NotificationDto>> INotificationQueries.ListRecentAsync(BranchCode? branch, int take, CancellationToken cancellationToken) =>
        Done<NotificationDto>(store.All<Notification>().Where(n => branch is null || n.BranchCode == branch).OrderByDescending(n => n.CreatedAt).Take(take)
            .Select(n => new NotificationDto(n.Type, n.BranchCode, n.Recipient, n.Message, n.CorrelationId, n.CreatedAt)));

    public Task<BranchSnapshot> GetBranchSnapshotAsync(BranchCode branch, DateTimeOffset? since, CancellationToken cancellationToken) =>
        // The in-memory store has no audit columns, so it always returns the full snapshot.
        Task.FromResult(new BranchSnapshot(
            branch,
            DateTimeOffset.UtcNow,
            store.All<Product>().OrderBy(p => p.Sku.Value, StringComparer.Ordinal).Select(p => p.ToDto()).ToList(),
            StockFor(branch)));

    public Task<IReadOnlyList<SyncConflictDto>> ListConflictsAsync(BranchCode? branch, int take, CancellationToken cancellationToken) =>
        Done<SyncConflictDto>(store.All<SyncInboxEntry>().Where(e => branch is null || e.BranchCode == branch)
            .SelectMany(e => e.Conflicts.Select(c => new SyncConflictDto(c.Id, e.DeviceId, e.LocalTransactionId, e.BranchCode, c.Type, c.ItemCode, c.Details, c.Status, c.RaisedAt, e.ServerTransactionId)))
            .OrderByDescending(c => c.RaisedAt).Take(take));

    private IReadOnlyList<StockDto> StockFor(BranchCode branch)
    {
        var products = store.All<Product>().ToDictionary(p => p.Sku);
        return store.All<BranchStock>().Where(s => s.BranchCode == branch)
            .Select(s => new StockDto(s.BranchCode, s.Sku, products.TryGetValue(s.Sku, out var p) ? p.Name : s.Sku.Value, s.QuantityOnHand, s.ReorderThreshold))
            .OrderBy(s => s.ProductName, StringComparer.Ordinal)
            .ToList();
    }

    private static bool InRange(DateTimeOffset at, DateOnly from, DateOnly to)
    {
        var day = DateOnly.FromDateTime(at.UtcDateTime);
        return day >= from && day <= to;
    }

    private static Task<IReadOnlyList<T>> Done<T>(IEnumerable<T> items) => Task.FromResult<IReadOnlyList<T>>(items.ToList());
}
