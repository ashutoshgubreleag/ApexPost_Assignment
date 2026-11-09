using ApexPost.Database;
using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Tracking;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Repositories.Queries;

/// <summary>Read side of the tracking API: no-tracking queries projected to DTOs, one round trip per call (no N+1).</summary>
public sealed class TrackingQueries(ApexPostDbContext db) : ITrackingQueries
{
    public async Task<PagedResult<TrackedShipmentDto>> ListAsync(TrackingListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var source = db.TrackedShipments.AsNoTracking().AsQueryable();
        if (query.Status is { } status)
        {
            source = source.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.City))
        {
            var city = query.City;
            source = source.Where(s => s.City == city);
        }

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await Project(source.OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id)
                .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new PagedResult<TrackedShipmentDto>(rows.Select(ToDto).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<TrackedShipmentDto?> GetAsync(string trackingNumber, CancellationToken cancellationToken)
    {
        var row = await Project(db.TrackedShipments.AsNoTracking().Where(s => s.TrackingNumber == trackingNumber))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row is null ? null : ToDto(row);
    }

    public async Task<IReadOnlyList<ScanDto>?> GetScansAsync(string trackingNumber, CancellationToken cancellationToken)
    {
        var shipmentId = await db.TrackedShipments.AsNoTracking()
            .Where(s => s.TrackingNumber == trackingNumber)
            .Select(s => (int?)s.Id)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (shipmentId is null)
        {
            return null;
        }

        var scans = await db.ShipmentScans.AsNoTracking()
            .Where(s => s.ShipmentId == shipmentId)
            .OrderBy(s => s.ScannedAt).ThenBy(s => s.Id)
            .Select(s => new { s.ScanId, s.Type, s.Location, s.ScannedAt, s.RecipientSignatureName })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return scans.Select(s => new ScanDto(s.ScanId, s.Type, s.Location, s.ScannedAt, s.RecipientSignatureName)).ToList();
    }

    internal sealed record LatestScanRow(Guid ScanId, ScanType Type, string Location, DateTimeOffset ScannedAt, string? RecipientSignatureName);

    internal sealed record ShipmentRow(
        string TrackingNumber,
        string RecipientName,
        string RecipientPhone,
        string AddressLine,
        string City,
        decimal WeightKg,
        TrackingStatus Status,
        DateTimeOffset CreatedAt,
        LatestScanRow? Latest);

    private static IQueryable<ShipmentRow> Project(IQueryable<TrackedShipment> source) =>
        source.Select(s => new ShipmentRow(
            s.TrackingNumber,
            s.RecipientName,
            s.RecipientPhone,
            s.AddressLine,
            s.City,
            s.WeightKg,
            s.Status,
            s.CreatedAt,
            s.Scans.OrderByDescending(x => x.ScannedAt).ThenByDescending(x => x.Id)
                .Select(x => new LatestScanRow(x.ScanId, x.Type, x.Location, x.ScannedAt, x.RecipientSignatureName))
                .FirstOrDefault()));

    private static TrackedShipmentDto ToDto(ShipmentRow r) => new(
        r.TrackingNumber, r.RecipientName, r.RecipientPhone, r.AddressLine, r.City, r.WeightKg, r.Status, r.CreatedAt,
        r.Latest is null ? null : new ScanDto(r.Latest.ScanId, r.Latest.Type, r.Latest.Location, r.Latest.ScannedAt, r.Latest.RecipientSignatureName));
}
