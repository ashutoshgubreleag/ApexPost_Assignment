using ApexPost.Models.Domain;

namespace ApexPost.Services.Tracking;

/// <summary>One page of results plus what the client needs to ask for the next page.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Filter for the shipment list. Page and size are clamped by the service, not trusted.</summary>
public sealed record TrackingListQuery(int Page = 1, int PageSize = TrackingLimits.DefaultPageSize, TrackingStatus? Status = null, string? City = null);

public static class TrackingLimits
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public const int MaxScansPerUpload = 100;
    public const string TrackingNumberPattern = @"^APX-\d{8}$";
    public const string PhonePattern = @"^\+?[0-9][0-9 ()\-]{6,18}[0-9]$";
}

public sealed record ShipmentDetails(string RecipientName, string RecipientPhone, string AddressLine, string City, decimal WeightKg);

public sealed record CreateTrackedShipmentCommand(string TrackingNumber, ShipmentDetails Details);

public sealed record ScanInput(Guid ScanId, ScanType ScanType, string Location, DateTimeOffset ScannedAt, string? RecipientSignatureName);

public sealed record ScanDto(Guid ScanId, ScanType ScanType, string Location, DateTimeOffset ScannedAt, string? RecipientSignatureName);

public sealed record TrackedShipmentDto(
    string TrackingNumber,
    string RecipientName,
    string RecipientPhone,
    string AddressLine,
    string City,
    decimal WeightKg,
    TrackingStatus Status,
    DateTimeOffset CreatedAt,
    ScanDto? LatestScan);

public sealed record RejectedScan(Guid ScanId, string Reason);

/// <summary>Outcome of a bulk scan upload (idempotent: re-sending the same batch only grows DuplicatesSkipped).</summary>
public sealed record ScanUploadResult(int Accepted, int DuplicatesSkipped, IReadOnlyList<RejectedScan> Rejected);
