using ApexPost.Courier.Offline.Application;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Harness;
using ApexPost.Courier.Offline.Infrastructure.Couchbase;

// Composition root and menu only. Usage: dotnet run --project ApexPost.Courier.Offline.Harness [dataDirectory]
// Everything here works with no network: the database is a local Couchbase Lite file.
var dataDirectory = args.Length > 0 ? args[0] : Path.Combine(Directory.GetCurrentDirectory(), "courier-data");

using var database = new CourierDatabase(dataDirectory);
var scans = new CouchbaseScanRepository(database);

// The monitor is attached to the database, which disposes it (removing the listener token) before closing.
var monitor = new PendingScanMonitor(database, scans, line => Console.WriteLine($"  [listener] {line}"));
monitor.Changed += (_, e) => Console.WriteLine($"  [listener] live pending scans: {e.PendingCount}");
monitor.Start();

var uploader = new FakeShipmentUploader("13");
var outbox = new OutboxService(scans, uploader);

Console.WriteLine($"ApexPost courier offline harness. Database '{CourierDatabase.DatabaseName}' at {database.DatabasePath}");
Console.WriteLine($"(the fake server rejects tracking numbers ending in '{uploader.FailureSuffix}')");

while (true)
{
    Console.WriteLine();
    Console.WriteLine($"=== Menu   (pending scans: {monitor.PendingCount}) ===");
    Console.WriteLine("1) Add Scan");
    Console.WriteLine("2) Find by Tracking Number");
    Console.WriteLine("3) List Pending");
    Console.WriteLine("4) Simulate Sync");
    Console.WriteLine("5) Delete Scan");
    Console.WriteLine("6) Exit");
    Console.WriteLine("-- extras: 7) Scan counts per type for a courier  8) Show query plans (indexes)  9) Add demo scans");
    Console.Write("> ");

    var choice = Console.ReadLine()?.Trim();
    if (choice is null || choice == "6")
    {
        break;
    }

    try
    {
        switch (choice)
        {
            case "1":
                AddScan();
                break;
            case "2":
                FindByTracking();
                break;
            case "3":
                ConsoleHelpers.PrintScans("Pending scans", scans.GetPending());
                break;
            case "4":
                await SimulateSyncAsync();
                break;
            case "5":
                DeleteScan();
                break;
            case "7":
                ShowCounts();
                break;
            case "8":
                ShowQueryPlans();
                break;
            case "9":
                AddDemoScans();
                break;
            default:
                Console.WriteLine("Unknown choice.");
                break;
        }
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

Console.WriteLine("Bye.");
return;

void AddScan()
{
    var tracking = ConsoleHelpers.Ask("Tracking number", "APX-10000001");
    if (tracking is null || !ConsoleHelpers.TryAskScanType(out var scanType))
    {
        Console.WriteLine("Cancelled or invalid scan type.");
        return;
    }

    var courier = ConsoleHelpers.Ask("Courier id", "C-100");
    var location = ConsoleHelpers.Ask("Location", "Depot Riverside");
    var notes = ConsoleHelpers.Ask("Notes (optional)", string.Empty);
    if (courier is null || location is null)
    {
        Console.WriteLine("Cancelled.");
        return;
    }

    var scan = scans.Create(ShipmentScan.CreateNew(tracking, scanType, courier, location, DateTimeOffset.UtcNow, notes));
    Console.WriteLine($"Saved locally as {scan.DocumentId} (syncStatus = {scan.SyncStatus}).");
}

void FindByTracking()
{
    var tracking = ConsoleHelpers.Ask("Tracking number", "APX-10000001");
    if (tracking is null)
    {
        return;
    }

    ConsoleHelpers.PrintScans($"Scans for {tracking}, ordered by scannedAtUtc", scans.GetByTrackingNumber(tracking));
}

async Task SimulateSyncAsync()
{
    var online = ConsoleHelpers.Ask("Network available? (y/n)", "y");
    uploader.Offline = !string.Equals(online, "y", StringComparison.OrdinalIgnoreCase);

    var result = await outbox.SyncPendingAsync();
    Console.WriteLine(
        $"Sync done: attempted {result.Attempted}, synced {result.Synced}, failed {result.Failed}" +
        (result.Interrupted ? $", INTERRUPTED ({result.Error}); remaining scans stay Pending." : "."));
    ConsoleHelpers.PrintScans("All scans after sync", scans.ListAll());
}

void DeleteScan()
{
    var input = ConsoleHelpers.Ask("Scan id (the Guid, or the full scan::{Guid} id)");
    if (input is null)
    {
        return;
    }

    var idText = input.StartsWith(ScanIds.Prefix, StringComparison.Ordinal) ? input[ScanIds.Prefix.Length..] : input;
    if (!Guid.TryParse(idText, out var scanId))
    {
        Console.WriteLine("That is not a valid scan id.");
        return;
    }

    Console.WriteLine(scans.Delete(scanId) ? "Deleted." : "No such scan.");
}

void ShowCounts()
{
    var courier = ConsoleHelpers.Ask("Courier id", "C-100");
    if (courier is null)
    {
        return;
    }

    Console.WriteLine($"--- Scans per type for courier {courier} ---");
    foreach (var (type, count) in scans.CountByScanType(courier))
    {
        Console.WriteLine($"{type,-10} {count}");
    }
}

void ShowQueryPlans()
{
    Console.WriteLine("--- Plan: scans by tracking number ---");
    Console.WriteLine(scans.ExplainTrackingNumberQuery());
    Console.WriteLine("--- Plan: pending scans ---");
    Console.WriteLine(scans.ExplainPendingQuery());
}

void AddDemoScans()
{
    var start = DateTimeOffset.UtcNow.AddHours(-6);

    // Inserted out of order on purpose: the tracking-number query must still return them by scannedAtUtc.
    scans.Create(ShipmentScan.CreateNew("APX-10000001", ScanType.Delivered, "C-100", "12 Fictional Lane", start.AddHours(5), "Left with neighbour"));
    scans.Create(ShipmentScan.CreateNew("APX-10000001", ScanType.Pickup, "C-100", "Depot Riverside", start));
    scans.Create(ShipmentScan.CreateNew("APX-10000001", ScanType.InTransit, "C-100", "Sorting Hub North", start.AddHours(2)));
    scans.Create(ShipmentScan.CreateNew("APX-10000013", ScanType.Pickup, "C-100", "Depot Riverside", start.AddHours(1)));
    scans.Create(ShipmentScan.CreateNew("APX-10000042", ScanType.Failed, "C-200", "5 Imaginary Road", start.AddHours(3), "Nobody home"));
    Console.WriteLine("Added 5 demo scans.");
}
