using System.Threading.Channels;
using ApexPost.CounterTerminal.Application;
using Couchbase.Lite;

namespace ApexPost.CounterTerminal.Infrastructure.Couchbase;

/// <summary>
/// Collection change listener on pendingTransactions (ADR-W3-09). Registered exactly once (a second
/// Start is a no-op); the callback does no work beyond copying the changed document IDs into an
/// unbounded channel, so the database thread is never blocked and no business logic runs there.
/// The SyncWorker reads the channel. Dispose removes the listener token before the database closes.
/// </summary>
public sealed class TerminalChangeMonitor(TerminalDatabase db) : IPendingSaleChangeFeed, IDisposable
{
    private readonly Channel<string> _changes = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Lock _gate = new();
    private ListenerToken? _token;
    private int _registrations;

    /// <summary>How many times a listener was actually registered (tests assert this stays 1).</summary>
    public int Registrations => _registrations;

    public void Start()
    {
        lock (_gate)
        {
            if (_token is not null)
            {
                return;
            }

            _token = db.Pending.AddChangeListener(OnChanged);
            _registrations++;
        }
    }

    public async ValueTask<IReadOnlyList<string>> WaitForChangesAsync(CancellationToken cancellationToken)
    {
        var first = await _changes.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var ids = new List<string> { first };
        while (_changes.Reader.TryRead(out var next))
        {
            ids.Add(next);
        }

        return ids;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _token?.Remove();
            _token = null;
        }

        _changes.Writer.TryComplete();
    }

    private void OnChanged(object? sender, CollectionChangedEventArgs e)
    {
        foreach (var id in e.DocumentIDs)
        {
            _changes.Writer.TryWrite(id);
        }
    }
}
