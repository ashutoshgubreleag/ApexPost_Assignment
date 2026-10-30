using System.Diagnostics;
using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Branch;
using ApexPost.Services.Common;
using ApexPost.Services.Notifications;
using ApexPost.Services.Reports;
using ApexPost.Services.Transactions;

namespace ApexPost.Services.Dashboard;

public sealed record BranchDashboard(
    BranchDto Branch,
    DailySalesRow Sales,
    BranchSalesSplit SalesSplit,
    DateOnly SalesDate,
    LowStockReport LowStock,
    IReadOnlyList<NotificationDto> Alerts,
    IReadOnlyList<TransactionSummaryDto> RecentTransactions,
    int OpenTransactionCount,
    long BuildMilliseconds);

public interface IDashboardService
{
    Task<BranchDashboard> BuildAsync(BranchCode branch, StaffContext actor, CancellationToken cancellationToken);
}

/// <summary>
/// Dashboard composition (query side only). The reads run one after another rather than with
/// Task.WhenAll: they share the request's single DbContext, which does not allow concurrent
/// operations. Each read is a small projection or stored procedure call, so this stays fast; the
/// total is measured and shown on the page. EF Core command logging proves it is a fixed number of
/// queries however many transactions the branch has (no N+1).
/// </summary>
public sealed class DashboardService(
    IBranchService branches,
    IReportService reports,
    IReportQueries reportQueries,
    INotificationService notifications,
    ITransactionService transactions) : IDashboardService
{
    public async Task<BranchDashboard> BuildAsync(BranchCode branch, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.CanReadBranch(branch))
        {
            throw new ForbiddenOperationException("You can only view your own branch dashboard.");
        }

        var stopwatch = Stopwatch.StartNew();
        var salesDate = await reports.GetLatestTradingDayAsync(cancellationToken).ConfigureAwait(false);
        var branchInfo = await branches.GetByCodeAsync(branch, cancellationToken).ConfigureAwait(false);
        var sales = await reports.GetDailySalesAsync(salesDate, branch, actor, cancellationToken).ConfigureAwait(false);
        var split = await reportQueries.GetBranchSalesSplitAsync(branch, salesDate, cancellationToken).ConfigureAwait(false);
        var lowStock = await reports.GetLowStockAsync(branch, null, actor, cancellationToken).ConfigureAwait(false);
        var alerts = await notifications.GetRecentAsync(branch, 8, cancellationToken).ConfigureAwait(false);
        var recent = await transactions.GetRecentAsync(actor, branch, 50, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        var row = sales.Rows.FirstOrDefault()
            ?? new DailySalesRow(branch, branchInfo.Name, 0, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, 0, 0);

        return new BranchDashboard(
            branchInfo,
            row,
            split,
            salesDate,
            lowStock,
            alerts,
            recent.Take(8).ToList(),
            recent.Count(t => t.Status == TransactionStatus.Open),
            stopwatch.ElapsedMilliseconds);
    }
}
