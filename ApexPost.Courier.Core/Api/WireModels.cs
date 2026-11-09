using System.Text.Json.Serialization;

namespace ApexPost.Courier.Core.Api;

/*
 * Wire shapes of the REST API (ApexPost.Api/Contracts). They are declared here, not shared, because the app
 * must keep working against the published contract (contracts/v2.json) and not against server internals.
 * Every type is registered in ApexJsonContext, so serialization is generated at build time (no reflection).
 */

/// <summary>Scan types as the server spells them. Part A's ScanType is mapped onto these by <see cref="ScanTypeMapper"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ApiScanType>))]
public enum ApiScanType
{
    PickedUp,
    Arrived,
    Departed,
    OutForDelivery,
    Delivered,
    DeliveryFailed,
}

public sealed record ScanItemWire(
    Guid ScanId,
    ApiScanType ScanType,
    string Location,
    DateTimeOffset ScannedAtUtc,
    string? RecipientSignatureName);

public sealed record ScanUploadWire(IReadOnlyList<ScanItemWire> Scans);

public sealed record RejectedScanWire(Guid ScanId, string Reason);

public sealed record ScanUploadResultWire(int Accepted, int DuplicatesSkipped, IReadOnlyList<RejectedScanWire> Rejected);

/// <summary>Body of <c>POST dev/token</c> (Development only): the courier id becomes the <c>courierId</c> claim.</summary>
public sealed record TokenRequestWire(string CourierId, string Role);

/// <summary>Answer of <c>POST dev/token</c>. <c>ExpiresAtUtc</c> may be missing.</summary>
public sealed record TokenResponseWire(string AccessToken, DateTimeOffset? ExpiresAtUtc);

/// <summary>The parts of an RFC 7807 Problem Details body the app shows.</summary>
public sealed record ProblemWire(string? Title, string? Detail, int? Status, Dictionary<string, string[]>? Errors);
