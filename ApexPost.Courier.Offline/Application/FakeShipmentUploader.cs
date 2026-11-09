using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Application;

/// <summary>
/// Stand-in for the real API (Part D replaces it). Deterministic: a scan whose tracking number ends with
/// <see cref="FailureSuffix"/> is rejected, every other scan is accepted. <see cref="Offline"/> simulates no network.
/// </summary>
public sealed class FakeShipmentUploader(string failureSuffix = "13") : IShipmentUploader
{
    public string FailureSuffix { get; } = failureSuffix;

    public bool Offline { get; set; }

    public Task<IReadOnlyList<ScanUploadOutcome>> UploadAsync(IReadOnlyList<ShipmentScan> batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();

        if (Offline)
        {
            throw new UploaderUnavailableException("Server unreachable (simulated offline).");
        }

        IReadOnlyList<ScanUploadOutcome> outcomes = batch
            .Select(scan => scan.TrackingNumber.EndsWith(FailureSuffix, StringComparison.Ordinal)
                ? new ScanUploadOutcome(scan.ScanId, false, "Rejected by the fake server (simulated).")
                : new ScanUploadOutcome(scan.ScanId, true))
            .ToList();
        return Task.FromResult(outcomes);
    }
}
