using System.Net;
using ApexPost.Courier.Offline.Domain;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ApexPost.Courier.Core.Api;

/// <summary>
/// The real <see cref="IShipmentUploader"/> (Part A had a fake). The server takes scans per tracking number, so the
/// batch is grouped by tracking number and each group is one request.
/// Outcomes: accepted or duplicate = Succeeded (idempotent: a re-sent scan is a success, never a second copy);
/// rejected or 400/403/404/409 = failed with the server's reason (retrying cannot help);
/// no connection, timeout, open circuit, 5xx or 401 = <see cref="UploaderUnavailableException"/>, so scans stay Pending.
/// </summary>
public sealed class ApiShipmentUploader(IServiceProvider services, Microsoft.Extensions.Options.IOptions<ApiOptions> options) : IShipmentUploader
{
    public async Task<IReadOnlyList<ScanUploadOutcome>> UploadAsync(IReadOnlyList<ShipmentScan> batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        // A fresh typed client per call (not one captured by this singleton) so the factory can rotate pooled handlers and DNS.
        var client = (ApexApiClient)services.GetService(typeof(ApexApiClient))!;
        var outcomes = new List<ScanUploadOutcome>(batch.Count);
        var maxPerRequest = Math.Clamp(options.Value.MaxScansPerRequest, 1, 100);

        foreach (var group in batch.GroupBy(s => s.TrackingNumber, StringComparer.Ordinal))
        {
            foreach (var chunk in group.Chunk(maxPerRequest))
            {
                outcomes.AddRange(await UploadChunkAsync(client, group.Key, chunk, cancellationToken).ConfigureAwait(false));
            }
        }

        return outcomes;
    }

    private async Task<List<ScanUploadOutcome>> UploadChunkAsync(ApexApiClient client, string trackingNumber, ShipmentScan[] chunk, CancellationToken cancellationToken)
    {
        var results = new List<ScanUploadOutcome>(chunk.Length);
        var sendable = new List<ScanItemWire>(chunk.Length);

        foreach (var scan in chunk)
        {
            // The server requires a signature name on Delivered scans; it is taken from the scan's Notes.
            // A Delivered scan without one can never be accepted, so it is failed locally instead of wasting a request.
            if (scan.ScanType == ScanType.Delivered && string.IsNullOrWhiteSpace(scan.Notes))
            {
                results.Add(new ScanUploadOutcome(scan.ScanId, false, "A Delivered scan needs the recipient's signature name."));
                continue;
            }

            sendable.Add(new ScanItemWire(
                scan.ScanId,
                ScanTypeMapper.ToApi(scan.ScanType),
                scan.Location,
                scan.ScannedAtUtc,
                scan.ScanType == ScanType.Delivered ? scan.Notes : null));
        }

        if (sendable.Count == 0)
        {
            return results;
        }

        try
        {
            var answer = await client.UploadScansAsync(trackingNumber, sendable, cancellationToken).ConfigureAwait(false);
            var rejected = answer.Rejected.ToDictionary(r => r.ScanId, r => r.Reason);
            foreach (var item in sendable)
            {
                results.Add(rejected.TryGetValue(item.ScanId, out var reason)
                    ? new ScanUploadOutcome(item.ScanId, false, reason)
                    : new ScanUploadOutcome(item.ScanId, true));
            }
        }
        catch (ApiException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict
                                          or HttpStatusCode.Forbidden or HttpStatusCode.UnprocessableEntity)
        {
            // The server understood and said no. Mark these scans failed so they stop blocking the queue.
            results.AddRange(sendable.Select(s => new ScanUploadOutcome(s.ScanId, false, ex.Message)));
        }
        catch (Exception ex) when (IsConnectivityFailure(ex, cancellationToken))
        {
            throw new UploaderUnavailableException(ex is BrokenCircuitException
                ? "The server keeps failing; the app is pausing uploads for a moment."
                : ex.Message);
        }

        return results;
    }

    private static bool IsConnectivityFailure(Exception ex, CancellationToken callerToken) => ex switch
    {
        HttpRequestException or TimeoutRejectedException or BrokenCircuitException or UnauthorizedAccessException => true,
        OperationCanceledException => !callerToken.IsCancellationRequested, // an HttpClient timeout, not the caller cancelling
        _ => false,
    };
}
