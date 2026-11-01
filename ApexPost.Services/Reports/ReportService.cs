using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Common;

namespace ApexPost.Services.Reports;

/// <summary>
/// Report use cases. This class owns the rules (who may see which branch, valid date ranges,
/// thresholds); the data comes from <see cref="IReportQueries"/>, which the infrastructure layer
/// answers with AsNoTracking projections and stored procedures. Nothing here loads entities.
/// </summary>
public sealed class ReportService(IReportQueries queries, ISyncQueries syncQueries, BusinessRules rules, TimeProvider clock) : IReportService
{
    public async Task<DateOnly> GetLatestTradingDayAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return await queries.GetLatestTradingDayAsync(today, cancellationToken).ConfigureAwait(false) ?? today;
    }

    public async Task<DailySalesReport> GetDailySalesAsync(DateOnly date, BranchCode? branch, StaffContext actor, CancellationToken cancellationToken)
    {
        var scope = Scope(branch, actor);
        var rows = await queries.GetDailySalesAsync(date, scope, cancellationToken).ConfigureAwait(false);
        return new DailySalesReport(date, rows);
    }

    public async Task<LowStockReport> GetLowStockAsync(BranchCode? branch, int? threshold, StaffContext actor, CancellationToken cancellationToken)
    {
        var scope = Scope(branch, actor);
        var limit = Math.Clamp(threshold ?? rules.DefaultLowStockThreshold, 0, 1000);
        var rows = await queries.GetLowStockAsync(scope, limit, cancellationToken).ConfigureAwait(false);
        return new LowStockReport(limit, rows);
    }

    public async Task<ShippingVolumeReport> GetShippingVolumeAsync(DateOnly from, DateOnly to, BranchCode? branch, StaffContext actor, CancellationToken cancellationToken)
    {
        EnsureRange(from, to);
        var scope = Scope(branch, actor);
        var rows = await queries.GetShippingVolumeAsync(from, to, scope, cancellationToken).ConfigureAwait(false);
        return new ShippingVolumeReport(from, to, rows);
    }

    public async Task<MoneyOrderAuditReport> GetMoneyOrderAuditAsync(DateOnly from, DateOnly to, BranchCode? branch, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureRange(from, to);
        if (actor.Role == StaffRole.CounterClerk)
        {
            throw new ForbiddenOperationException("The money order audit view is for auditors, managers and administrators.");
        }

        var scope = Scope(branch, actor);
        var data = await queries.GetMoneyOrderAuditAsync(from, to, scope, rules.HighValueMoneyOrderAmount.Amount, cancellationToken).ConfigureAwait(false);
        return new MoneyOrderAuditReport(from, to, data.Rows, data.MoneyOrders, data.Verifications);
    }

    public Task<IReadOnlyList<SyncConflictDto>> GetSyncConflictsAsync(BranchCode? branch, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Role == StaffRole.CounterClerk)
        {
            throw new ForbiddenOperationException("Sync conflicts are reviewed by branch managers, auditors and administrators.");
        }

        return syncQueries.ListConflictsAsync(Scope(branch, actor), 200, cancellationToken);
    }

    /// <summary>Clerks and managers see their own branch; auditors and admins may pick any (or all).</summary>
    internal static BranchCode? Scope(BranchCode? requested, StaffContext actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.IsAdministrator || actor.IsAuditor)
        {
            return requested;
        }

        if (requested is { } code && code != actor.BranchCode)
        {
            throw new ForbiddenOperationException("You can only report on your own branch.");
        }

        return actor.BranchCode;
    }

    private static void EnsureRange(DateOnly from, DateOnly to)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366)
        {
            throw new CommandValidationException("to", "Choose a date range of up to one year with 'to' on or after 'from'.");
        }
    }
}
