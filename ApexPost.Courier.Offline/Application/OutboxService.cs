using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Application;

/// <summary>Summary of one outbox run.</summary>
/// <param name="Attempted">Scans the uploader returned an outcome for.</param>
/// <param name="Synced">Scans now marked Synced.</param>
/// <param name="Failed">Scans now marked Failed (the server rejected them).</param>
/// <param name="Interrupted">True when the run stopped early because the server could not be reached; the rest stay Pending.</param>
/// <param name="Error">Why the run was interrupted, if it was.</param>
public sealed record OutboxSyncResult(int Attempted, int Synced, int Failed, bool Interrupted, string? Error);

/// <summary>
/// The outbox: reads Pending scans from the local store in batches, hands them to the uploader and marks each
/// one Synced or Failed. A scan the server could not be asked about stays Pending, so nothing is ever lost
/// because of a bad network. Cancellation is not a failure: it propagates, and unprocessed scans stay Pending.
/// </summary>
public sealed class OutboxService
{
    public const int DefaultBatchSize = 20;

    private readonly IScanRepository _scans;
    private readonly IShipmentUploader _uploader;

    public OutboxService(IScanRepository scans, IShipmentUploader uploader, int batchSize = 20)
    {
        ArgumentNullException.ThrowIfNull(scans);
        ArgumentNullException.ThrowIfNull(uploader);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        _scans = scans;
        _uploader = uploader;
        BatchSize = batchSize;
    }

    public int BatchSize { get; }

    public async Task<OutboxSyncResult> SyncPendingAsync(CancellationToken cancellationToken = default)
    {
        var attempted = 0;
        var synced = 0;
        var failed = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = _scans.GetPending(BatchSize);
            if (batch.Count == 0)
            {
                return new OutboxSyncResult(attempted, synced, failed, false, null);
            }

            IReadOnlyList<ScanUploadOutcome> outcomes;
            try
            {
                outcomes = await _uploader.UploadAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                // Server unreachable (or a timeout inside the uploader): keep the scans Pending and stop.
                return new OutboxSyncResult(attempted, synced, failed, true, ex.Message);
            }

            var processed = 0;
            foreach (var scan in batch)
            {
                var outcome = outcomes.FirstOrDefault(o => o.ScanId == scan.ScanId);
                if (outcome is null)
                {
                    continue;
                }

                _scans.Update(scan with { SyncStatus = outcome.Succeeded ? SyncStatus.Synced : SyncStatus.Failed });
                processed++;
                attempted++;
                if (outcome.Succeeded)
                {
                    synced++;
                }
                else
                {
                    failed++;
                }
            }

            if (processed == 0)
            {
                // The uploader answered but did not mention any scan of the batch: stop instead of looping forever.
                return new OutboxSyncResult(attempted, synced, failed, true, "The uploader returned no outcome for the batch.");
            }
        }
    }
}
