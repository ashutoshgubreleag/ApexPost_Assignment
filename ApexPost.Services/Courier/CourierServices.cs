using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Common;

namespace ApexPost.Services.Courier;

/// <summary>Simulated courier partner API for live fuel surcharge rates.</summary>
public interface ICourierRateService
{
    Task<decimal> GetFuelSurchargeRateAsync(ZoneDto zone, CancellationToken cancellationToken);
}

/// <summary>
/// The partner's rate API allows only a few concurrent calls per client. A
/// <see cref="SemaphoreSlim"/> bounds concurrency: under load, extra callers wait
/// asynchronously (no blocked threads) instead of hammering the partner and being throttled.
/// </summary>
public sealed class SimulatedCourierRateService : ICourierRateService, IDisposable
{
    private readonly SemaphoreSlim _throttle;
    private readonly CourierOptions _options;
    private int _inFlight;
    private int _peakInFlight;

    public SimulatedCourierRateService(IOptions<CourierOptions> options)
    {
        _options = options.Value;
        _throttle = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentRateLookups));
    }

    /// <summary>Highest number of simultaneous calls observed (lets tests prove the bound holds).</summary>
    public int PeakConcurrency => Volatile.Read(ref _peakInFlight);

    public async Task<decimal> GetFuelSurchargeRateAsync(ZoneDto zone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zone);
        await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = Interlocked.Increment(ref _inFlight);
            UpdatePeak(now);
            await Task.Delay(_options.RateLookupLatencyMs, cancellationToken).ConfigureAwait(false);

            return zone switch
            {
                { IsInternational: true } => 0.12m,
                { IsRemote: true } => 0.10m,
                _ => 0.08m,
            };
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
            _throttle.Release();
        }
    }

    public void Dispose() => _throttle.Dispose();

    private void UpdatePeak(int candidate)
    {
        int peak;
        while (candidate > (peak = Volatile.Read(ref _peakInFlight)))
        {
            if (Interlocked.CompareExchange(ref _peakInFlight, candidate, peak) == peak)
            {
                return;
            }
        }
    }
}

/// <summary>Outcome of a tracking lookup. Pattern-matched by the integration controller.</summary>
public abstract record TrackingResult
{
    public sealed record Found(string TrackingNumber, string Status, string LastScanLocation, DateTimeOffset EstimatedDelivery) : TrackingResult;
    public sealed record NotFound(string TrackingNumber) : TrackingResult;
    public sealed record Unavailable(string TrackingNumber, string Reason) : TrackingResult;
}

public interface ICourierTrackingService
{
    Task<TrackingResult> TrackAsync(string trackingNumber, CancellationToken cancellationToken);
}

/// <summary>
/// Simulated courier tracking with a timeout.
/// <para><b>Cancellation vs failure:</b> two different things can cancel the lookup.</para>
/// <list type="bullet">
/// <item>The <i>caller</i> gives up (browser closed, request aborted): we rethrow
/// <see cref="OperationCanceledException"/>. Nobody is waiting, so nothing is logged as an error.</item>
/// <item>Our own <i>timeout</i> fires: the caller is still waiting, so this is a failure of the
/// dependency and we return <see cref="TrackingResult.Unavailable"/> (the API answers 503).</item>
/// </list>
/// A linked token source combines both; <c>when (!cancellationToken.IsCancellationRequested)</c>
/// tells them apart.
/// </summary>
public sealed partial class SimulatedCourierTrackingService(
    ITransactionQueries shipments,
    IOptions<CourierOptions> options,
    TimeProvider clock,
    ILogger<SimulatedCourierTrackingService> logger) : ICourierTrackingService
{
    private readonly CourierOptions _options = options.Value;

    public async Task<TrackingResult> TrackAsync(string trackingNumber, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(_options.TrackingTimeoutMs));

        try
        {
            var latency = trackingNumber.EndsWith(_options.SlowTrackingSuffix, StringComparison.Ordinal)
                ? _options.TrackingTimeoutMs * 4
                : _options.TrackingLatencyMs;
            await Task.Delay(latency, timeout.Token).ConfigureAwait(false);

            var shipment = await shipments.GetShipmentByTrackingNumberAsync(trackingNumber, timeout.Token).ConfigureAwait(false);
            if (shipment is null)
            {
                return new TrackingResult.NotFound(trackingNumber);
            }

            var status = shipment.Status switch
            {
                ShipmentStatus.Lodged when clock.GetUtcNow() - shipment.CreatedAt > TimeSpan.FromDays(3) => "Delivered",
                ShipmentStatus.Lodged => "In transit",
                ShipmentStatus.Cancelled => "Cancelled",
                _ => "Awaiting lodgement",
            };
            var eta = shipment.CreatedAt.AddDays(shipment.ServiceLevel == ShippingServiceLevel.Express ? 1 : 3);

            return new TrackingResult.Found(shipment.TrackingNumber, status, $"{shipment.BranchCode} sorting facility", eta);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTrackingTimeout(trackingNumber, _options.TrackingTimeoutMs);
            return new TrackingResult.Unavailable(trackingNumber, "The courier tracking service did not respond in time.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Courier tracking for {TrackingNumber} timed out after {TimeoutMs} ms")]
    private partial void LogTrackingTimeout(string trackingNumber, int timeoutMs);
}
