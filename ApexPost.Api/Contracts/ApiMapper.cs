using ApexPost.Services.Tracking;

namespace ApexPost.Api.Contracts;

/// <summary>Maps between API contracts and service models. The only place that knows both shapes.</summary>
internal static class ApiMapper
{
    public static ShipmentDetails ToDetails(CreateShipmentRequest r) =>
        new(r.RecipientName, r.RecipientPhone, r.Address, r.City, r.WeightKg);

    public static ShipmentDetails ToDetails(UpdateShipmentRequest r) =>
        new(r.RecipientName, r.RecipientPhone, r.Address, r.City, r.WeightKg);

    public static ScanInput ToInput(ScanItemRequest r) =>
        new(r.ScanId, r.ScanType!.Value, r.Location, r.ScannedAtUtc!.Value, r.RecipientSignatureName);

    public static ShipmentResponse ToResponse(TrackedShipmentDto s) =>
        new(s.TrackingNumber, s.RecipientName, s.RecipientPhone, s.AddressLine, s.City, s.WeightKg, s.Status, s.CreatedAt);

    public static ShipmentPageResponse ToResponse(PagedResult<TrackedShipmentDto> page) =>
        new(page.Items.Select(ToResponse).ToList(), page.Page, page.PageSize, page.TotalCount, page.TotalPages);

    public static ScanResponse ToResponse(ScanDto s) =>
        new(s.ScanId, s.ScanType, s.Location, s.ScannedAt, s.RecipientSignatureName);

    public static ScanUploadResponse ToResponse(ScanUploadResult r) =>
        new(r.Accepted, r.DuplicatesSkipped, r.Rejected.Select(x => new RejectedScanResponse(x.ScanId, x.Reason)).ToList());
}
