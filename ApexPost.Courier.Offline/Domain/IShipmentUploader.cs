namespace ApexPost.Courier.Offline.Domain;

/// <summary>Result of uploading one scan: it was accepted by the server, or it was rejected with a reason.</summary>
public sealed record ScanUploadOutcome(Guid ScanId, bool Succeeded, string? Error = null);

/// <summary>
/// Port to the server. Part A uses a fake; Part D replaces it with the real API call.
/// Throw when the server cannot be reached at all (the scans then stay Pending); return an outcome per scan otherwise.
/// </summary>
public interface IShipmentUploader
{
    Task<IReadOnlyList<ScanUploadOutcome>> UploadAsync(IReadOnlyList<ShipmentScan> batch, CancellationToken cancellationToken);
}

/// <summary>The server cannot be reached (offline, timeout, circuit open). Scans must stay Pending.</summary>
public sealed class UploaderUnavailableException(string message) : Exception(message);
