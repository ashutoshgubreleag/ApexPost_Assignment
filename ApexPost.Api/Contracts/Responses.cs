using ApexPost.Models.Domain;

namespace ApexPost.Api.Contracts;

// Scan contracts are shared by v1 and v2: they are unchanged between the versions.

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
