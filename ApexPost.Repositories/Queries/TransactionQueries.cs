using ApexPost.Database;
using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Repositories.Persistence;
using ApexPost.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Repositories.Queries;

public sealed class TransactionQueries(ApexPostDbContext db) : ITransactionQueries
{
    /// <summary>
    /// Recent baskets: a single projection. ItemCount and the payment method are correlated
    /// sub-queries inside the same SELECT (see PERFORMANCE.md, query Q1), not one query per row.
    /// </summary>
    public async Task<IReadOnlyList<TransactionSummaryDto>> ListRecentAsync(BranchCode? branch, int take, CancellationToken cancellationToken) =>
        await RecentQuery(branch, take).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<TransactionSummaryDto?> GetSummaryAsync(int transactionId, CancellationToken cancellationToken) =>
        Summaries(db.CounterTransactions.AsNoTracking().Where(t => t.Id == transactionId)).SingleOrDefaultAsync(cancellationToken);

    /// <summary>The query behind the "recent transactions" list (exposed so tests can capture ToQueryString).</summary>
    public IQueryable<TransactionSummaryDto> RecentQuery(BranchCode? branch, int take) =>
        Summaries(db.CounterTransactions.AsNoTracking()
            .Where(t => branch == null || t.BranchCode == branch)
            .OrderByDescending(t => t.CreatedAt)
            .Take(take));

    public async Task<TransactionDetailsData?> GetDetailsAsync(int transactionId, CancellationToken cancellationToken)
    {
        // Lines and payments are two collections of the same parent: AsSplitQuery sends one SQL
        // statement per collection instead of a JOIN that would return lines x payments rows.
        var tx = await db.CounterTransactions.AsNoTracking()
            .Include(t => t.LineItems)
            .Include(t => t.Payments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(t => t.Id == transactionId, cancellationToken).ConfigureAwait(false);
        if (tx is null)
        {
            return null;
        }

        var shipments = await db.Shipments.AsNoTracking()
            .Where(s => s.TransactionId == transactionId)
            .OrderBy(s => s.Id)
            .Select(s => new ShipmentDto(s.Id, s.TrackingNumber, s.TransactionId, s.BranchCode, s.ShippingType, s.ServiceLevel, s.ZoneCode, s.Weight, s.Dimensions, s.ChargeableWeight, s.Recipient, s.Price, s.Status, s.CreatedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var moneyOrders = await db.MoneyOrders.AsNoTracking()
            .Where(m => m.TransactionId == transactionId)
            .OrderBy(m => m.Id)
            .Select(m => new MoneyOrderDto(m.Id, m.SerialNumber, m.TransactionId, m.BranchCode, m.Amount, m.Fee, m.Sender, m.Recipient, m.IdentityVerificationId, m.Status, m.CreatedAt, m.IssuedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var verificationRecords = await db.IdentityVerifications.AsNoTracking()
            .Where(v => v.TransactionId == transactionId)
            .OrderBy(v => v.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new TransactionDetailsData(
            ToSummary(tx),
            tx.LineItems.OrderBy(l => l.LineNumber).Select(l => new TransactionLineDto(l.LineNumber, l.LineType, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.TaxCategory, l.TaxAmount, l.LineTotal, l.PassThroughAmount)).ToList(),
            tx.Payments.Select(p => new PaymentDto(p.Method, p.Amount, p.PaidAt)).ToList(),
            shipments,
            moneyOrders,
            verificationRecords.Select(IdentityVerificationMapper.ToDto).ToList());
    }

    public Task<ShipmentDto?> GetShipmentAsync(int shipmentId, CancellationToken cancellationToken) =>
        ShipmentProjection(db.Shipments.AsNoTracking().Where(s => s.Id == shipmentId)).SingleOrDefaultAsync(cancellationToken);

    public Task<ShipmentDto?> GetShipmentByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken)
    {
        var normalised = (trackingNumber ?? string.Empty).Trim().ToUpperInvariant();
        return ShipmentProjection(db.Shipments.AsNoTracking().Where(s => s.TrackingNumber == normalised)).SingleOrDefaultAsync(cancellationToken);
    }

    public Task<MoneyOrderDto?> GetMoneyOrderAsync(int moneyOrderId, CancellationToken cancellationToken) =>
        db.MoneyOrders.AsNoTracking()
            .Where(m => m.Id == moneyOrderId)
            .Select(m => new MoneyOrderDto(m.Id, m.SerialNumber, m.TransactionId, m.BranchCode, m.Amount, m.Fee, m.Sender, m.Recipient, m.IdentityVerificationId, m.Status, m.CreatedAt, m.IssuedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<MoneyOrderDto>> ListMoneyOrdersAsync(BranchCode? branch, int take, CancellationToken cancellationToken) =>
        await db.MoneyOrders.AsNoTracking()
            .Where(m => branch == null || m.BranchCode == branch)
            .OrderByDescending(m => m.CreatedAt)
            .Take(take)
            .Select(m => new MoneyOrderDto(m.Id, m.SerialNumber, m.TransactionId, m.BranchCode, m.Amount, m.Fee, m.Sender, m.Recipient, m.IdentityVerificationId, m.Status, m.CreatedAt, m.IssuedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    private static IQueryable<ShipmentDto> ShipmentProjection(IQueryable<Shipment> source) =>
        source.Select(s => new ShipmentDto(s.Id, s.TrackingNumber, s.TransactionId, s.BranchCode, s.ShippingType, s.ServiceLevel, s.ZoneCode, s.Weight, s.Dimensions, s.ChargeableWeight, s.Recipient, s.Price, s.Status, s.CreatedAt));

    private static IQueryable<TransactionSummaryDto> Summaries(IQueryable<CounterTransaction> source) =>
        source.Select(t => new TransactionSummaryDto(
            t.Id,
            t.BranchCode,
            t.CounterNumber,
            t.ClerkStaffId,
            t.Source,
            t.Status,
            t.LineItems.Sum(l => l.Quantity),
            t.Subtotal,
            t.TaxAmount,
            t.PassThroughAmount,
            t.TotalAmount,
            t.Payments.OrderByDescending(p => p.Id).Select(p => (PaymentMethod?)p.Method).FirstOrDefault(),
            t.VoidReason,
            t.AuthorisedByStaffId,
            t.CreatedAt,
            t.ClosedAt));

    private static TransactionSummaryDto ToSummary(CounterTransaction t) => new(
        t.Id, t.BranchCode, t.CounterNumber, t.ClerkStaffId, t.Source, t.Status, t.ItemCount, t.Subtotal, t.TaxAmount,
        t.PassThroughAmount, t.TotalAmount, t.PaymentMethod, t.VoidReason, t.AuthorisedByStaffId, t.CreatedAt, t.ClosedAt);
}
