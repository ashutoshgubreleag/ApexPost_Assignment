using ApexPost.Database;
using ApexPost.Database.Configurations;
using ApexPost.Database.Records;
using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Repositories.Persistence;

/*
 * Specific repositories, one per aggregate. They load tracked aggregates (Include only what the
 * aggregate needs) and stage new ones with Add. None of them calls SaveChanges and none returns
 * IQueryable: the unit of work commits, and screens read through the query services.
 */

public sealed class BranchRepository(ApexPostDbContext db) : IBranchRepository
{
    public Task<Branch?> GetByCodeAsync(BranchCode code, CancellationToken cancellationToken) =>
        db.Branches.Include(b => b.Counters).SingleOrDefaultAsync(b => b.Code == code, cancellationToken);

    public Task<bool> ExistsAsync(BranchCode code, CancellationToken cancellationToken) =>
        db.Branches.AnyAsync(b => b.Code == code, cancellationToken);

    public void Add(Branch branch) => db.Branches.Add(branch);
}

public sealed class StaffRepository(ApexPostDbContext db) : IStaffRepository
{
    public Task<StaffMember?> GetByIdAsync(int staffId, CancellationToken cancellationToken) =>
        db.Staff.SingleOrDefaultAsync(s => s.Id == staffId, cancellationToken);

    public Task<StaffMember?> GetByEmailAsync(string normalisedEmail, CancellationToken cancellationToken) =>
        db.Staff.SingleOrDefaultAsync(s => s.Email == normalisedEmail, cancellationToken);

    public Task<bool> EmailInUseAsync(string normalisedEmail, int exceptStaffId, CancellationToken cancellationToken) =>
        db.Staff.AnyAsync(s => s.Email == normalisedEmail && s.Id != exceptStaffId, cancellationToken);

    public void Add(StaffMember staff) => db.Staff.Add(staff);
}

public sealed class ProductRepository(ApexPostDbContext db) : IProductRepository
{
    public Task<Product?> GetBySkuAsync(Sku sku, CancellationToken cancellationToken) =>
        db.Products.SingleOrDefaultAsync(p => p.Sku == sku, cancellationToken);

    public async Task<IReadOnlyList<Product>> GetBySkusAsync(IReadOnlyCollection<Sku> skus, CancellationToken cancellationToken) =>
        await db.Products.Where(p => skus.Contains(p.Sku)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<int> RevisePricesAsync(ProductCategory category, decimal factor, CancellationToken cancellationToken) =>
        // One set-based UPDATE ... SET UnitPrice = ROUND(UnitPrice * @factor, 2), ModifiedAtUtc = ...
        // WHERE CategoryId = @category AND IsActive = 1. No entities are loaded or tracked.
        db.Products
            .Where(p => p.Category == category && p.IsActive)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => EF.Property<decimal>(p, ProductColumns.UnitPrice), p => Math.Round(EF.Property<decimal>(p, ProductColumns.UnitPrice) * factor, 2))
                    .SetProperty(p => EF.Property<DateTime?>(p, "ModifiedAtUtc"), _ => DateTime.UtcNow),
                cancellationToken);
}

public sealed class StockRepository(ApexPostDbContext db) : IStockRepository
{
    public async Task<IReadOnlyList<BranchStock>> GetForUpdateAsync(BranchCode branch, IReadOnlyCollection<Sku> skus, CancellationToken cancellationToken) =>
        await db.BranchStock.Where(s => s.BranchCode == branch && skus.Contains(s.Sku)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<int> GetQuantityAsync(BranchCode branch, Sku sku, CancellationToken cancellationToken) =>
        await db.BranchStock.AsNoTracking()
            .Where(s => s.BranchCode == branch && s.Sku == sku)
            .Select(s => (int?)s.QuantityOnHand)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? 0;

    public void Add(BranchStock stock) => db.BranchStock.Add(stock);
}

public sealed class CounterTransactionRepository(ApexPostDbContext db) : ICounterTransactionRepository
{
    public Task<CounterTransaction?> GetAsync(int transactionId, CancellationToken cancellationToken) =>
        // A tracked aggregate: lines and payments are needed to apply the domain rules. Two
        // collection Includes in one SQL query would multiply rows (lines x payments), so split.
        db.CounterTransactions
            .Include(t => t.LineItems)
            .Include(t => t.Payments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(t => t.Id == transactionId, cancellationToken);

    public void Add(CounterTransaction transaction) => db.CounterTransactions.Add(transaction);
}

public sealed class ShipmentRepository(ApexPostDbContext db) : IShipmentRepository
{
    public async Task<IReadOnlyList<Shipment>> GetByTrackingNumbersAsync(IReadOnlyCollection<string> trackingNumbers, CancellationToken cancellationToken) =>
        trackingNumbers.Count == 0
            ? []
            : await db.Shipments.Where(s => trackingNumbers.Contains(s.TrackingNumber)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Shipment shipment) => db.Shipments.Add(shipment);
}

public sealed class ShippingZoneRepository(ApexPostDbContext db) : IShippingZoneRepository
{
    public Task<ShippingZone?> GetByCodeAsync(string zoneCode, CancellationToken cancellationToken) =>
        db.ShippingZones.SingleOrDefaultAsync(z => z.ZoneCode == zoneCode, cancellationToken);
}

public sealed class MoneyOrderRepository(ApexPostDbContext db) : IMoneyOrderRepository
{
    public Task<MoneyOrder?> GetByIdAsync(int moneyOrderId, CancellationToken cancellationToken) =>
        db.MoneyOrders.SingleOrDefaultAsync(m => m.Id == moneyOrderId, cancellationToken);

    public async Task<IReadOnlyList<MoneyOrder>> GetBySerialNumbersAsync(IReadOnlyCollection<string> serialNumbers, CancellationToken cancellationToken) =>
        serialNumbers.Count == 0
            ? []
            : await db.MoneyOrders.Where(m => serialNumbers.Contains(m.SerialNumber)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(MoneyOrder moneyOrder) => db.MoneyOrders.Add(moneyOrder);
}

public sealed class NotificationRepository(ApexPostDbContext db) : INotificationRepository
{
    public void Add(Notification notification) => db.Notifications.Add(notification);
}

public sealed class SyncInboxRepository(ApexPostDbContext db) : ISyncInboxRepository
{
    public Task<SyncInboxEntry?> FindAsync(string deviceId, Guid localTransactionId, CancellationToken cancellationToken) =>
        db.SyncInbox
            .Include(s => s.Conflicts)
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.DeviceId == deviceId && s.LocalTransactionId == localTransactionId, cancellationToken);

    public void Add(SyncInboxEntry entry) => db.SyncInbox.Add(entry);
}

/// <summary>
/// Repository over the separate persistence model (ADR-W3-05). The domain object never touches
/// the table: the mapper converts it to <see cref="IdentityVerificationRecord"/> (last four
/// characters, legacy codes) and back. Generated keys are copied back after the commit.
/// </summary>
public sealed class IdentityVerificationRepository : IIdentityVerificationRepository
{
    private readonly ApexPostDbContext _db;
    private readonly List<(IdentityVerification Domain, IdentityVerificationRecord Record)> _pending = [];

    public IdentityVerificationRepository(ApexPostDbContext db)
    {
        _db = db;
        _db.SavedChanges += (_, _) =>
        {
            foreach (var (domain, record) in _pending)
            {
                domain.AssignId(record.Id);
            }

            _pending.Clear();
        };
    }

    public async Task<IReadOnlyList<IdentityVerification>> GetByReferencesAsync(IReadOnlyCollection<string> references, CancellationToken cancellationToken)
    {
        if (references.Count == 0)
        {
            return [];
        }

        var records = await _db.IdentityVerifications.AsNoTracking()
            .Where(v => references.Contains(v.Reference))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return records.Select(IdentityVerificationMapper.ToDomain).ToList();
    }

    public void Add(IdentityVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        var record = IdentityVerificationMapper.ToRecord(verification);
        _db.IdentityVerifications.Add(record);
        _pending.Add((verification, record));
    }
}
