using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Infrastructure.Couchbase;

namespace ApexPost.Courier.Offline.Tests;

/// <summary>A real Couchbase Lite database in a throw-away directory, with the repository wired up.</summary>
public sealed class CourierTestHarness : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private CourierDatabase? _database;

    public CourierTestHarness()
    {
        Directory = Path.Combine(Path.GetTempPath(), "apexpost-courier-tests", Guid.NewGuid().ToString("N"));
        Open();
    }

    public string Directory { get; }

    public CourierDatabase Database => _database!;

    public CouchbaseScanRepository Scans { get; private set; } = null!;

    /// <summary>Closes and reopens the database from disk (simulates an app restart).</summary>
    public void Reopen()
    {
        _database?.Dispose();
        Open();
    }

    public static ShipmentScan NewScan(
        string tracking = "APX-10000001",
        ScanType type = ScanType.Pickup,
        string courier = "C-100",
        int minutes = 0,
        string location = "Depot Riverside") =>
        ShipmentScan.CreateNew(tracking, type, courier, location, Start.AddMinutes(minutes));

    public void Dispose()
    {
        _database?.Dispose();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the OS may still hold a file handle for a moment.
        }
    }

    private void Open()
    {
        _database = new CourierDatabase(Directory);
        Scans = new CouchbaseScanRepository(_database);
    }
}
