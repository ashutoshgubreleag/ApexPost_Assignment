using Couchbase.Lite;
using Couchbase.Lite.Logging;
using Couchbase.Lite.Query;

namespace ApexPost.Courier.Offline.Infrastructure.Couchbase;

/// <summary>
/// Opens the courier's local Couchbase Lite database <c>apexpost_courier</c> and exposes its DEFAULT collection.
/// Creates the value indexes used by the SQL++ queries (CreateIndex is idempotent). Anything registered with
/// <see cref="Attach"/> (for example the change listener) is disposed before the database closes, in reverse order.
/// </summary>
public sealed class CourierDatabase : IDisposable
{
    public const string DatabaseName = "apexpost_courier";
    public const int SchemaVersion = 1;
    public const string TrackingIndexName = "idx_scans_trackingNumber";
    public const string SyncStatusIndexName = "idx_scans_syncStatus";

    /// <summary>How SQL++ addresses the default collection (kept in one place so it can be changed in one edit).</summary>
    public const string QuerySource = "_";

    private readonly Database _database;
    private readonly List<IDisposable> _attached = [];
    private readonly object _gate = new();
    private bool _disposed;

    static CourierDatabase()
    {
        // Keep LiteCore's own console output to warnings and errors.
        LogSinks.Console = new ConsoleLogSink(LogLevel.Warning);
    }

    public CourierDatabase(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        _database = new Database(DatabaseName, new DatabaseConfiguration { Directory = Path.GetFullPath(directory) });

        Scans = _database.GetDefaultCollection() ?? throw new InvalidOperationException("The default collection is not available.");
        Scans.CreateIndex(TrackingIndexName, new ValueIndexConfiguration(["trackingNumber"]));
        Scans.CreateIndex(SyncStatusIndexName, new ValueIndexConfiguration(["syncStatus"]));
    }

    public string DatabasePath => _database.Path ?? string.Empty;

    /// <summary>The default collection that holds every scan document.</summary>
    public Collection Scans { get; }

    public IQuery CreateQuery(string sqlPlusPlus) => _database.CreateQuery(sqlPlusPlus);

    /// <summary>Registers something to dispose (newest first) before the database closes.</summary>
    public void Attach(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
        {
            if (!_attached.Contains(resource))
            {
                _attached.Add(resource);
            }
        }
    }

    public void Dispose()
    {
        List<IDisposable> toDispose;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            toDispose = [.. _attached];
            _attached.Clear();
        }

        for (var i = toDispose.Count - 1; i >= 0; i--)
        {
            toDispose[i].Dispose();
        }

        Scans.Dispose();
        _database.Close();
        _database.Dispose();
    }
}
