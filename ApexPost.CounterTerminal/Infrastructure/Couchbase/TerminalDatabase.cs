using Couchbase.Lite;
using Couchbase.Lite.Logging;
using Couchbase.Lite.Query;

namespace ApexPost.CounterTerminal.Infrastructure.Couchbase;

/// <summary>
/// Opens the terminal's Couchbase Lite database in a persistent, configurable directory and
/// exposes its four collections (scope "apexpost"). Creates the value indexes used by the SQL++
/// queries. Dispose closes the database; the change monitor must be disposed first (it removes
/// its listener token before the collection goes away).
/// </summary>
public sealed class TerminalDatabase : IDisposable
{
    public const string ScopeName = "apexpost";
    public const string CatalogCollection = "catalog";
    public const string StockCollection = "stock";
    public const string PendingCollection = "pendingTransactions";
    public const string SyncStateCollection = "syncState";
    public const int SchemaVersion = 1;

    private readonly Database _database;
    private bool _disposed;

    static TerminalDatabase()
    {
        // Keep LiteCore's own console output to warnings and errors.
        LogSinks.Console = new ConsoleLogSink(LogLevel.Warning);
    }

    public TerminalDatabase(string directory, string name = "apexpost-terminal")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        _database = new Database(name, new DatabaseConfiguration { Directory = Path.GetFullPath(directory) });

        Catalog = _database.CreateCollection(CatalogCollection, ScopeName);
        Stock = _database.CreateCollection(StockCollection, ScopeName);
        Pending = _database.CreateCollection(PendingCollection, ScopeName);
        SyncState = _database.CreateCollection(SyncStateCollection, ScopeName);

        // Indexes for the filtered/sorted fields of the SQL++ queries (CreateIndex is idempotent).
        Catalog.CreateIndex("idx_catalog_type_category_name", new ValueIndexConfiguration(["type", "category", "name"]));
        Pending.CreateIndex("idx_pending_type_status_captured", new ValueIndexConfiguration(["type", "status", "capturedAt"]));
        Pending.CreateIndex("idx_pending_type_businessdate", new ValueIndexConfiguration(["type", "businessDate"]));
    }

    public string DatabasePath => _database.Path ?? string.Empty;

    public Collection Catalog { get; }

    public Collection Stock { get; }

    public Collection Pending { get; }

    public Collection SyncState { get; }

    public IQuery CreateQuery(string sqlPlusPlus) => _database.CreateQuery(sqlPlusPlus);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Catalog.Dispose();
        Stock.Dispose();
        Pending.Dispose();
        SyncState.Dispose();
        _database.Close();
        _database.Dispose();
    }
}
