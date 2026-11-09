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

    // ---- version 1 (deprecated): flat recipient, numeric status ----

    public static V1.ShipmentResponse ToV1(TrackedShipmentDto s) =>
        new(s.TrackingNumber, s.RecipientName, s.RecipientPhone, s.AddressLine, s.City, s.WeightKg, (int)s.Status, s.CreatedAt);

    public static V1.ShipmentPageResponse ToV1(PagedResult<TrackedShipmentDto> page) =>
        new(page.Items.Select(ToV1).ToList(), page.Page, page.PageSize, page.TotalCount, page.TotalPages);

    // ---- version 2: nested recipient, latest scan, string status ----

    public static V2.ShipmentResponse ToV2(TrackedShipmentDto s) =>
        new(
            s.TrackingNumber,
            new V2.RecipientResponse(s.RecipientName, s.RecipientPhone, s.AddressLine, s.City),
            s.WeightKg,
            s.Status,
            s.CreatedAt,
            s.LatestScan is null ? null : new V2.LatestScanResponse(s.LatestScan.ScanType, s.LatestScan.Location, s.LatestScan.ScannedAt));

    public static V2.ShipmentPageResponse ToV2(PagedResult<TrackedShipmentDto> page) =>
        new(page.Items.Select(ToV2).ToList(), page.Page, page.PageSize, page.TotalCount, page.TotalPages);

    // ---- scans: identical in both versions ----

    public static ScanResponse ToResponse(ScanDto s) =>
        new(s.ScanId, s.ScanType, s.Location, s.ScannedAt, s.RecipientSignatureName);

    public static ScanUploadResponse ToResponse(ScanUploadResult r) =>
        new(r.Accepted, r.DuplicatesSkipped, r.Rejected.Select(x => new RejectedScanResponse(x.ScanId, x.Reason)).ToList());
}
