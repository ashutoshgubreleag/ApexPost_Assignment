namespace ApexPost.Api.Contracts.V1;

/// <summary>A shipment as returned by API version 1 (deprecated): flat recipient fields and a numeric status.</summary>
/// <param name="TrackingNumber">Tracking number, for example <c>APX-10000001</c>.</param>
/// <param name="RecipientName">Name of the person receiving the parcel.</param>
/// <param name="RecipientPhone">Recipient phone number.</param>
/// <param name="Address">Delivery street address.</param>
/// <param name="City">Delivery city.</param>
/// <param name="WeightKg">Parcel weight in kilograms.</param>
/// <param name="Status">Lifecycle status as a number: 0 Created, 1 PickedUp, 2 InTransit, 3 OutForDelivery, 4 Delivered, 5 Failed.</param>
/// <param name="CreatedAtUtc">When the shipment was created (UTC).</param>
public sealed record ShipmentResponse(
    string TrackingNumber,
    string RecipientName,
    string RecipientPhone,
    string Address,
    string City,
    decimal WeightKg,
    int Status,
    DateTimeOffset CreatedAtUtc);

/// <summary>One page of shipments (version 1).</summary>
/// <param name="Items">The shipments on this page.</param>
/// <param name="Page">The page number returned (1-based).</param>
/// <param name="PageSize">The page size used (never more than 50).</param>
/// <param name="TotalCount">How many shipments match the filter in total.</param>
/// <param name="TotalPages">How many pages there are at this page size.</param>
public sealed record ShipmentPageResponse(IReadOnlyList<ShipmentResponse> Items, int Page, int PageSize, int TotalCount, int TotalPages);
