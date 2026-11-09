using ApexPost.Database;
using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Repositories.Persistence;

/// <summary>Repository for the courier-tracking aggregate. Stages changes only; the unit of work commits.</summary>
public sealed class TrackedShipmentRepository(ApexPostDbContext db) : ITrackedShipmentRepository
{
    public Task<TrackedShipment?> GetByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken) =>
        db.TrackedShipments.SingleOrDefaultAsync(s => s.TrackingNumber == trackingNumber, cancellationToken);

    public Task<bool> ExistsAsync(string trackingNumber, CancellationToken cancellationToken) =>
        db.TrackedShipments.AnyAsync(s => s.TrackingNumber == trackingNumber, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetScanOwnersAsync(IReadOnlyCollection<Guid> scanIds, CancellationToken cancellationToken) =>
        await db.ShipmentScans.AsNoTracking()
            .Where(s => scanIds.Contains(s.ScanId))
            .ToDictionaryAsync(s => s.ScanId, s => s.ShipmentId, cancellationToken)
            .ConfigureAwait(false);

    public void Add(TrackedShipment shipment) => db.TrackedShipments.Add(shipment);

    public void Remove(TrackedShipment shipment) => db.TrackedShipments.Remove(shipment);
}
