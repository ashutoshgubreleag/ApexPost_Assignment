using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Reports;

namespace ApexPost.Services.Abstractions;

/*
 * Query side (CQS). Screens, reports and the dashboard read through these interfaces. The EF Core
 * implementations use AsNoTracking projections straight into DTOs, or stored procedures, and never
 * load entities they do not need. Writes go through command services + repositories + IUnitOfWork.
 * Both sides use the same SQL Server database (no read replica): this is query/command
 * separation in code, not separate data stores.
 */

public interface IBranchQueries
{
    Task<IReadOnlyList<BranchDto>> ListAsync(CancellationToken cancellationToken);

    Task<BranchDto?> GetAsync(BranchCode code, CancellationToken cancellationToken);
}

public interface IStaffQueries
{
    Task<IReadOnlyList<StaffDto>> ListAsync(BranchCode? branch, CancellationToken cancellationToken);

    Task<StaffDto?> GetAsync(int staffId, CancellationToken cancellationToken);
}

public interface ICatalogQueries
{
    Task<IReadOnlyList<ProductDto>> ListProductsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ZoneDto>> ListZonesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<StockDto>> GetStockForBranchAsync(BranchCode branch, CancellationToken cancellationToken);
}

public interface ITransactionQueries
{
    Task<IReadOnlyList<TransactionSummaryDto>> ListRecentAsync(BranchCode? branch, int take, CancellationToken cancellationToken);

    Task<TransactionSummaryDto?> GetSummaryAsync(int transactionId, CancellationToken cancellationToken);

    /// <summary>Header, lines, payments, shipments, money orders and verifications for one basket.</summary>
    Task<TransactionDetailsData?> GetDetailsAsync(int transactionId, CancellationToken cancellationToken);

    Task<ShipmentDto?> GetShipmentAsync(int shipmentId, CancellationToken cancellationToken);

    Task<ShipmentDto?> GetShipmentByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken);

    Task<MoneyOrderDto?> GetMoneyOrderAsync(int moneyOrderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MoneyOrderDto>> ListMoneyOrdersAsync(BranchCode? branch, int take, CancellationToken cancellationToken);
}

/// <summary>Raw data for the basket screen; the service adds the rule-based flags.</summary>
public sealed record TransactionDetailsData(
    TransactionSummaryDto Transaction,
    IReadOnlyList<TransactionLineDto> Lines,
    IReadOnlyList<PaymentDto> Payments,
    IReadOnlyList<ShipmentDto> Shipments,
    IReadOnlyList<MoneyOrderDto> MoneyOrders,
    IReadOnlyList<VerificationDto> Verifications);

public interface IReportQueries
{
    /// <summary>Most recent day (not after <paramref name="today"/>) with a paid transaction.</summary>
    Task<DateOnly?> GetLatestTradingDayAsync(DateOnly today, CancellationToken cancellationToken);

    Task<IReadOnlyList<DailySalesRow>> GetDailySalesAsync(DateOnly date, BranchCode? branch, CancellationToken cancellationToken);

    /// <summary>Calls usp_DailyBranchSales (retail vs services for one branch and day).</summary>
    Task<BranchSalesSplit> GetBranchSalesSplitAsync(BranchCode branch, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Calls usp_LowStockReport (optional branch and threshold).</summary>
    Task<IReadOnlyList<LowStockRow>> GetLowStockAsync(BranchCode? branch, int threshold, CancellationToken cancellationToken);

    Task<IReadOnlyList<ShippingVolumeRow>> GetShippingVolumeAsync(DateOnly from, DateOnly to, BranchCode? branch, CancellationToken cancellationToken);

    Task<MoneyOrderAuditData> GetMoneyOrderAuditAsync(DateOnly from, DateOnly to, BranchCode? branch, decimal highValueThreshold, CancellationToken cancellationToken);
}

/// <summary>Result of usp_DailyBranchSales.</summary>
public sealed record BranchSalesSplit(BranchCode BranchCode, DateOnly Date, int PaidTransactions, Money RetailTotal, Money ServicesTotal, Money TaxTotal, Money GrandTotal);

public sealed record MoneyOrderAuditData(
    IReadOnlyList<MoneyOrderAuditRow> Rows,
    IReadOnlyList<MoneyOrderDto> MoneyOrders,
    IReadOnlyList<VerificationDto> Verifications);

public interface INotificationQueries
{
    Task<IReadOnlyList<NotificationDto>> ListRecentAsync(BranchCode? branch, int take, CancellationToken cancellationToken);
}

public interface ISyncQueries
{
    /// <summary>Catalogue, prices and branch stock changed since <paramref name="since"/> (all when null).</summary>
    Task<BranchSnapshot> GetBranchSnapshotAsync(BranchCode branch, DateTimeOffset? since, CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncConflictDto>> ListConflictsAsync(BranchCode? branch, int take, CancellationToken cancellationToken);
}

public sealed record BranchSnapshot(
    BranchCode BranchCode,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ProductDto> Products,
    IReadOnlyList<StockDto> Stock);
