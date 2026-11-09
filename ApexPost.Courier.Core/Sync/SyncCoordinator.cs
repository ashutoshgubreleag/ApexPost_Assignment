using ApexPost.Courier.Core.Resilience;
using ApexPost.Courier.Offline.Application;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Core.Sync;

/// <summary>What the Sync Status page shows.</summary>
public sealed record SyncSnapshot(
    bool IsOnline,
    bool IsSyncing,
    int PendingCount,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc,
    string? LastMessage,
    CircuitStateKind Circuit);

/// <summary>
/// Runs the outbox when it makes sense: on a timer, when the device comes back online, and on demand.
/// One sync at a time (a semaphore), nothing while offline, and the circuit breaker decides when the server is
/// given a rest. Saving a scan never waits for any of this.
/// </summary>
public sealed class SyncCoordinator : IDisposable
{
    private readonly OutboxService _outbox;
    private readonly IScanRepository _scans;
    private readonly INetworkStatus _connectivity;
    private readonly ICircuitStateMonitor _circuit;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Lock _snapshotLock = new();
    private SyncSnapshot _snapshot;

    public SyncCoordinator(OutboxService outbox, IScanRepository scans, INetworkStatus connectivity, ICircuitStateMonitor circuit, TimeProvider? clock = null)
    {
        _outbox = outbox;
        _scans = scans;
        _connectivity = connectivity;
        _circuit = circuit;
        _clock = clock ?? TimeProvider.System;
        _snapshot = new SyncSnapshot(connectivity.IsOnline, false, scans.CountPending(), null, null, null, circuit.State);
        _connectivity.OnlineChanged += OnOnlineChanged;
        _circuit.StateChanged += OnCircuitChanged;
    }

    public event EventHandler<SyncSnapshot>? Changed;

    public SyncSnapshot Current => _snapshot;

    /// <summary>Refreshes the pending count (call after saving a scan) and asks the background loop to try soon.</summary>
    public void NotifyScanSaved()
    {
        Publish(s => s with { PendingCount = _scans.CountPending() });
        Wake();
    }

    /// <summary>Syncs now. Returns null when it did not run (offline, or another sync is already running).</summary>
    public async Task<OutboxSyncResult?> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_connectivity.IsOnline)
        {
            Publish(s => s with { IsOnline = false, PendingCount = _scans.CountPending(), LastMessage = "Offline: scans are saved on this phone." });
            return null;
        }

        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            Publish(s => s with { IsSyncing = true, IsOnline = true });
            var now = _clock.GetUtcNow();
            var result = await _outbox.SyncPendingAsync(cancellationToken).ConfigureAwait(false);
            var message = result.Interrupted
                ? $"Server unavailable: {result.Error}. Scans stay on this phone."
                : result.Attempted == 0
                    ? "Nothing to upload."
                    : $"Uploaded {result.Synced}, rejected {result.Failed}.";
            Publish(s => s with
            {
                IsSyncing = false,
                PendingCount = _scans.CountPending(),
                LastAttemptUtc = now,
                LastSuccessUtc = result.Interrupted ? s.LastSuccessUtc : now,
                LastMessage = message,
            });
            return result;
        }
        catch (OperationCanceledException)
        {
            Publish(s => s with { IsSyncing = false });
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Background loop: sync every <paramref name="interval"/>, sooner when a scan is saved or the network returns.</summary>
    public async Task RunAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SyncNowAsync(cancellationToken).ConfigureAwait(false);
                await _wake.WaitAsync(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _connectivity.OnlineChanged -= OnOnlineChanged;
        _circuit.StateChanged -= OnCircuitChanged;
        _gate.Dispose();
        _wake.Dispose();
    }

    private void OnOnlineChanged(object? sender, bool online)
    {
        Publish(s => s with { IsOnline = online, LastMessage = online ? "Back online." : "Offline: scans are saved on this phone." });
        if (online)
        {
            Wake();
        }
    }

    private void OnCircuitChanged(object? sender, CircuitStateKind state) => Publish(s => s with { Circuit = state });

    private void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending.
        }
    }

    private void Publish(Func<SyncSnapshot, SyncSnapshot> change)
    {
        SyncSnapshot updated;
        lock (_snapshotLock)
        {
            _snapshot = updated = change(_snapshot);
        }

        Changed?.Invoke(this, updated);
    }
}
