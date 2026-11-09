using ApexPost.Courier.Offline.Domain;
using Couchbase.Lite;

namespace ApexPost.Courier.Offline.Infrastructure.Couchbase;

/// <summary>Raised after the collection changed: which documents changed and how many scans are Pending now.</summary>
public sealed class PendingScanChangedEventArgs(IReadOnlyList<string> documentIds, int pendingCount) : EventArgs
{
    public IReadOnlyList<string> DocumentIds { get; } = documentIds;

    public int PendingCount { get; } = pendingCount;
}

/// <summary>
/// Collection change listener on the default collection. It logs the ids of the changed documents, keeps a live
/// count of Pending scans and raises <see cref="Changed"/>. It is registered once (a second Start is a no-op) and
/// the listener token is removed in <see cref="Dispose"/>, which <see cref="CourierDatabase"/> calls before it closes.
/// </summary>
public sealed class PendingScanMonitor(CourierDatabase db, IScanRepository scans, Action<string>? log = null) : IDisposable
{
    private readonly object _gate = new();
    private ListenerToken? _token;
    private int _registrations;
    private int _pendingCount;
    private bool _disposed;
    private volatile bool _stopped;

    public event EventHandler<PendingScanChangedEventArgs>? Changed;

    /// <summary>The live number of Pending scans (updated by the listener).</summary>
    public int PendingCount => Volatile.Read(ref _pendingCount);

    /// <summary>How many times a listener was actually registered (tests assert this stays 1).</summary>
    public int Registrations => Volatile.Read(ref _registrations);

    public bool IsListening
    {
        get
        {
            lock (_gate)
            {
                return _token is not null;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_token is not null)
            {
                return;
            }

            Volatile.Write(ref _pendingCount, scans.CountPending());
            _token = db.Scans.AddChangeListener(OnChanged);
            _registrations++;
        }

        db.Attach(this);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopped = true;
            _token?.Remove();
            _token = null;
        }
    }

    private void OnChanged(object? sender, CollectionChangedEventArgs e)
    {
        if (_stopped)
        {
            return;
        }

        var ids = e.DocumentIDs.ToList();
        log?.Invoke($"changed documents: {string.Join(", ", ids)}");

        int count;
        try
        {
            count = scans.CountPending();
        }
        catch (Exception ex)
        {
            log?.Invoke($"could not refresh the pending count: {ex.Message}");
            return;
        }

        Volatile.Write(ref _pendingCount, count);
        Changed?.Invoke(this, new PendingScanChangedEventArgs(ids, count));
    }
}
