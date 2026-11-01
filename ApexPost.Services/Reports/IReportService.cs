using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Common;

namespace ApexPost.Services.Reports;

public sealed record DailySalesRow(
    BranchCode BranchCode,
    string BranchName,
    int TransactionCount,
    Money RetailNet,
    Money ServicesNet,
    Money Tax,
    Money PassThrough,
    Money Total,
    int VoidedCount,
    int RefundedCount);

public sealed record DailySalesReport(DateOnly Date, IReadOnlyList<DailySalesRow> Rows)
{
    public Money RetailNet => Rows.Select(r => r.RetailNet).Total();
    public Money ServicesNet => Rows.Select(r => r.ServicesNet).Total();
    public Money Tax => Rows.Select(r => r.Tax).Total();
    public Money Total => Rows.Select(r => r.Total).Total();
    public int TransactionCount => Rows.Sum(r => r.TransactionCount);
}

public sealed record LowStockRow(BranchCode BranchCode, Sku Sku, string ProductName, ProductCategory Category, int QuantityOnHand, int ReorderThreshold)
{
    public int SuggestedReorder => Math.Max(ReorderThreshold * 2 - QuantityOnHand, 0);
}

public sealed record LowStockReport(int Threshold, IReadOnlyList<LowStockRow> Rows);

public sealed record ShippingVolumeRow(string ZoneCode, string ZoneName, ShippingServiceLevel ServiceLevel, int Count, Weight TotalWeight, Money Revenue);

public sealed record ShippingVolumeReport(DateOnly From, DateOnly To, IReadOnlyList<ShippingVolumeRow> Rows)
{
    public int TotalCount => Rows.Sum(r => r.Count);
    public Money TotalRevenue => Rows.Select(r => r.Revenue).Total();
}

public sealed record MoneyOrderAuditRow(
    BranchCode BranchCode,
    string BranchName,
    int MoneyOrderCount,
    Money FaceValue,
    Money Fees,
    int HighValueCount,
    int VerificationsPassed,
    int VerificationsFailed);

public sealed record MoneyOrderAuditReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<MoneyOrderAuditRow> Rows,
    IReadOnlyList<MoneyOrderDto> MoneyOrders,
    IReadOnlyList<VerificationDto> Verifications);

public interface IReportService
{
    /// <summary>Most recent UTC day with paid transactions on or before today (so a demo always shows data).</summary>
    Task<DateOnly> GetLatestTradingDayAsync(CancellationToken cancellationToken);

    Task<DailySalesReport> GetDailySalesAsync(DateOnly date, BranchCode? branch, StaffContext actor, CancellationToken cancellationToken);

    Task<LowStockReport> GetLowStockAsync(BranchCode? branch, int? threshold, StaffContext actor, CancellationToken cancellationToken);

    Task<ShippingVolumeReport> GetShippingVolumeAsync(DateOnly from, DateOnly to, BranchCode? branch, StaffContext actor, CancellationToken cancellationToken);

    Task<MoneyOrderAuditReport> GetMoneyOrderAuditAsync(DateOnly from, DateOnly to, BranchCode? branch, StaffContext actor, CancellationToken cancellationToken);

    /// <summary>Offline-sync conflicts (price variance, stock shortfall) waiting for review. Not for clerks.</summary>
    Task<IReadOnlyList<SyncConflictDto>> GetSyncConflictsAsync(BranchCode? branch, StaffContext actor, CancellationToken cancellationToken);
}
