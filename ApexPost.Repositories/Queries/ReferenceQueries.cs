using ApexPost.Database;
using ApexPost.Database.Configurations;
using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Repositories.Queries;

/*
 * Read side. Every query is AsNoTracking and projects (Select) straight into a DTO, so only the
 * columns a screen shows are read and nothing is change-tracked. Each method issues a fixed number
 * of SQL statements regardless of how many rows come back (no N+1).
 */

public sealed class BranchQueries(ApexPostDbContext db) : IBranchQueries
{
    public async Task<IReadOnlyList<BranchDto>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Branches.AsNoTracking()
            .OrderBy(b => b.Code)
            .Select(b => new
            {
                b.Id,
                b.Code,
                b.Name,
                b.Region,
                b.OpeningHours,
                b.Status,
                Counters = b.Counters.OrderBy(c => c.Number).Select(c => new CounterDto(c.Number, c.Name, c.IsActive)).ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(b => new BranchDto(b.Id, b.Code, b.Name, b.Region, b.OpeningHours, b.Status, b.Counters)).ToList();
    }

    public async Task<BranchDto?> GetAsync(BranchCode code, CancellationToken cancellationToken)
    {
        var b = await db.Branches.AsNoTracking()
            .Where(x => x.Code == code)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.Region,
                x.OpeningHours,
                x.Status,
                Counters = x.Counters.OrderBy(c => c.Number).Select(c => new CounterDto(c.Number, c.Name, c.IsActive)).ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return b is null ? null : new BranchDto(b.Id, b.Code, b.Name, b.Region, b.OpeningHours, b.Status, b.Counters);
    }
}

public sealed class StaffQueries(ApexPostDbContext db) : IStaffQueries
{
    public async Task<IReadOnlyList<StaffDto>> ListAsync(BranchCode? branch, CancellationToken cancellationToken) =>
        await db.Staff.AsNoTracking()
            .Where(s => branch == null || s.HomeBranchCode == branch)
            .OrderBy(s => s.HomeBranchCode).ThenBy(s => s.Name)
            .Select(s => new StaffDto(s.Id, s.Name, s.Email, s.Role, s.HomeBranchCode, s.IsActive))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<StaffDto?> GetAsync(int staffId, CancellationToken cancellationToken) =>
        db.Staff.AsNoTracking()
            .Where(s => s.Id == staffId)
            .Select(s => new StaffDto(s.Id, s.Name, s.Email, s.Role, s.HomeBranchCode, s.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
}

public sealed class CatalogQueries(ApexPostDbContext db) : ICatalogQueries
{
    public async Task<IReadOnlyList<ProductDto>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Products.AsNoTracking()
            .OrderBy(p => p.Sku)
            .Select(p => new { p.Id, p.Sku, p.Name, p.Category, UnitPrice = EF.Property<decimal>(p, ProductColumns.UnitPrice), p.TaxCategory, p.IsActive })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(p => new ProductDto(p.Id, p.Sku, p.Name, p.Category, new Money(p.UnitPrice), p.TaxCategory, p.IsActive)).ToList();
    }

    public async Task<IReadOnlyList<ZoneDto>> ListZonesAsync(CancellationToken cancellationToken)
    {
        var zones = await db.ShippingZones.AsNoTracking()
            .OrderBy(z => z.ZoneCode)
            .Select(z => new
            {
                z.ZoneCode,
                z.Name,
                z.IsInternational,
                z.IsRemote,
                Letter = z.Rates.Where(r => r.ShippingType == ShippingType.Letter).Select(r => r.BaseRate).FirstOrDefault(),
                Parcel = z.Rates.Where(r => r.ShippingType == ShippingType.Parcel).Select(r => new { r.BaseRate, r.PerKgRate }).FirstOrDefault(),
            })
            .IgnoreAutoIncludes()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return zones.Select(z => new ZoneDto(z.ZoneCode, z.Name, z.IsInternational, z.IsRemote, z.Letter, z.Parcel?.BaseRate ?? Money.Zero, z.Parcel?.PerKgRate ?? Money.Zero)).ToList();
    }

    public async Task<IReadOnlyList<StockDto>> GetStockForBranchAsync(BranchCode branch, CancellationToken cancellationToken) =>
        await StockForBranchQuery(branch).ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// One query: stock joined to products for the name (no per-row product lookups), projected
    /// straight to the DTO. Exposed so tests can capture its SQL with ToQueryString.
    /// </summary>
    public IQueryable<StockDto> StockForBranchQuery(BranchCode branch) =>
        from s in db.BranchStock.AsNoTracking()
        join p in db.Products.AsNoTracking() on s.Sku equals p.Sku
        where s.BranchCode == branch
        orderby p.Name
        select new StockDto(s.BranchCode, s.Sku, p.Name, s.QuantityOnHand, s.ReorderThreshold);
}

public sealed class NotificationQueries(ApexPostDbContext db) : INotificationQueries
{
    public async Task<IReadOnlyList<NotificationDto>> ListRecentAsync(BranchCode? branch, int take, CancellationToken cancellationToken) =>
        await db.Notifications.AsNoTracking()
            .Where(n => branch == null || n.BranchCode == branch)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new NotificationDto(n.Type, n.BranchCode, n.Recipient, n.Message, n.CorrelationId, n.CreatedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}
