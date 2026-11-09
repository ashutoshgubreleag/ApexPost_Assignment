using ApexPost.Models.Domain;

namespace ApexPost.Api.Contracts;

/// <summary>A shipment as returned by the API.</summary>
/// <param name="TrackingNumber">Tracking number, for example <c>APX-10000001</c>.</param>
/// <param name="RecipientName">Name of the person receiving the parcel.</param>
/// <param name="RecipientPhone">Recipient phone number.</param>
/// <param name="Address">Delivery street address.</param>
/// <param name="City">Delivery city.</param>
/// <param name="WeightKg">Parcel weight in kilograms.</param>
/// <param name="Status">Lifecycle status (Created, PickedUp, InTransit, OutForDelivery, Delivered, Failed).</param>
/// <param name="CreatedAtUtc">When the shipment was created (UTC).</param>
public sealed record ShipmentResponse(
    string TrackingNumber,
    string RecipientName,
    string RecipientPhone,
    string Address,
    string City,
    decimal WeightKg,
    TrackingStatus Status,
    DateTimeOffset CreatedAtUtc);

/// <summary>One page of shipments.</summary>
/// <param name="Items">The shipments on this page.</param>
/// <param name="Page">The page number returned (1-based).</param>
/// <param name="PageSize">The page size used (never more than 50).</param>
/// <param name="TotalCount">How many shipments match the filter in total.</param>
/// <param name="TotalPages">How many pages there are at this page size.</param>
public sealed record ShipmentPageResponse(IReadOnlyList<ShipmentResponse> Items, int Page, int PageSize, int TotalCount, int TotalPages);

/// <summary>One scan in a shipment's history.</summary>
/// <param name="ScanId">The scan id generated on the courier's device.</param>
/// <param name="ScanType">What the scan recorded.</param>
/// <param name="Location">Where the scan happened.</param>
/// <param name="ScannedAtUtc">When the scan happened (UTC).</param>
/// <param name="RecipientSignatureName">Signature name, present on delivered scans.</param>
public sealed record ScanResponse(Guid ScanId, ScanType ScanType, string Location, DateTimeOffset ScannedAtUtc, string? RecipientSignatureName);

/// <summary>A scan that was not stored, and why.</summary>
/// <param name="ScanId">The scan id from the request.</param>
/// <param name="Reason">Human-readable reason.</param>
public sealed record RejectedScanResponse(Guid ScanId, string Reason);

/// <summary>Result of a bulk scan upload.</summary>
/// <param name="Accepted">Scans stored by this request.</param>
/// <param name="DuplicatesSkipped">Scans ignored because their scan id was already stored (or repeated in the request).</param>
/// <param name="Rejected">Scans that could not be stored, with reasons.</param>
public sealed record ScanUploadResponse(int Accepted, int DuplicatesSkipped, IReadOnlyList<RejectedScanResponse> Rejected);
