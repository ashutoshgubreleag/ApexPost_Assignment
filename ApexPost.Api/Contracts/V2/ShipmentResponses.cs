using ApexPost.Models.Domain;

namespace ApexPost.Api.Contracts.V2;

/// <summary>Who the parcel is for and where it goes (nested in version 2; flat in version 1).</summary>
/// <param name="Name">Name of the person receiving the parcel.</param>
/// <param name="Phone">Recipient phone number.</param>
/// <param name="Address">Delivery street address.</param>
/// <param name="City">Delivery city.</param>
public sealed record RecipientResponse(string Name, string Phone, string Address, string City);

/// <summary>The most recent scan of a shipment, embedded in the shipment so one call shows where it is.</summary>
/// <param name="ScanType">What the scan recorded.</param>
/// <param name="Location">Where the scan happened.</param>
/// <param name="ScannedAtUtc">When the scan happened (UTC).</param>
public sealed record LatestScanResponse(ScanType ScanType, string Location, DateTimeOffset ScannedAtUtc);

/// <summary>A shipment as returned by API version 2: nested recipient, latest scan, and status as a string.</summary>
/// <param name="TrackingNumber">Tracking number, for example <c>APX-10000001</c>.</param>
/// <param name="Recipient">Recipient name, phone and delivery address.</param>
/// <param name="WeightKg">Parcel weight in kilograms.</param>
/// <param name="Status">Lifecycle status as a string: Created, PickedUp, InTransit, OutForDelivery, Delivered or Failed.</param>
/// <param name="CreatedAtUtc">When the shipment was created (UTC).</param>
/// <param name="LatestScan">The most recent scan, or null when the shipment has not been scanned yet.</param>
public sealed record ShipmentResponse(
    string TrackingNumber,
    RecipientResponse Recipient,
    decimal WeightKg,
    TrackingStatus Status,
    DateTimeOffset CreatedAtUtc,
    LatestScanResponse? LatestScan);

/// <summary>One page of shipments (version 2).</summary>
/// <param name="Items">The shipments on this page.</param>
/// <param name="Page">The page number returned (1-based).</param>
/// <param name="PageSize">The page size used (never more than 50).</param>
/// <param name="TotalCount">How many shipments match the filter in total.</param>
/// <param name="TotalPages">How many pages there are at this page size.</param>
public sealed record ShipmentPageResponse(IReadOnlyList<ShipmentResponse> Items, int Page, int PageSize, int TotalCount, int TotalPages);
