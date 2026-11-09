using ApexPost.Models.Domain;
using ApexPost.Services.Tracking;

namespace ApexPost.Services.Abstractions;

/// <summary>Persistence port for the <see cref="TrackedShipment"/> aggregate (stages changes; the unit of work commits).</summary>
public interface ITrackedShipmentRepository
{
    /// <summary>Loads a tracked shipment (without its scans) for modification, or null.</summary>
    Task<TrackedShipment?> GetByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string trackingNumber, CancellationToken cancellationToken);

    /// <summary>For each of <paramref name="scanIds"/> that is already stored, the id of the shipment that owns it.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetScanOwnersAsync(IReadOnlyCollection<Guid> scanIds, CancellationToken cancellationToken);

    void Add(TrackedShipment shipment);

    void Remove(TrackedShipment shipment);
}

/// <summary>Read-side queries for the tracking API (no tracking, projected straight to DTOs).</summary>
public interface ITrackingQueries
{
    Task<PagedResult<TrackedShipmentDto>> ListAsync(TrackingListQuery query, CancellationToken cancellationToken);

    Task<TrackedShipmentDto?> GetAsync(string trackingNumber, CancellationToken cancellationToken);

    /// <summary>Scan history, oldest first, or null when the shipment does not exist.</summary>
    Task<IReadOnlyList<ScanDto>?> GetScansAsync(string trackingNumber, CancellationToken cancellationToken);
}

/// <summary>Use cases of the courier tracking API. Every business rule of Part B lives behind this interface.</summary>
public interface IShipmentTrackingService
{
    Task<PagedResult<TrackedShipmentDto>> ListAsync(TrackingListQuery query, CancellationToken cancellationToken);

    /// <exception cref="Models.Exceptions.EntityNotFoundException">Unknown tracking number.</exception>
    Task<TrackedShipmentDto> GetAsync(string trackingNumber, CancellationToken cancellationToken);

    /// <exception cref="Models.Exceptions.ConflictException">The tracking number already exists.</exception>
    Task<TrackedShipmentDto> CreateAsync(CreateTrackedShipmentCommand command, CancellationToken cancellationToken);

    Task<TrackedShipmentDto> UpdateAsync(string trackingNumber, ShipmentDetails details, CancellationToken cancellationToken);

    Task<TrackedShipmentDto> ChangeStatusAsync(string trackingNumber, TrackingStatus status, CancellationToken cancellationToken);

    Task DeleteAsync(string trackingNumber, CancellationToken cancellationToken);

    Task<ScanUploadResult> UploadScansAsync(string trackingNumber, IReadOnlyList<ScanInput> scans, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScanDto>> GetScansAsync(string trackingNumber, CancellationToken cancellationToken);
}
