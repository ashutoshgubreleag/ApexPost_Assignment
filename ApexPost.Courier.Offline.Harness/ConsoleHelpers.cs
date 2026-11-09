using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Harness;

internal static class ConsoleHelpers
{
    public static string? Ask(string label, string? defaultValue = null)
    {
        Console.Write(defaultValue is null ? $"{label}: " : $"{label} [{defaultValue}]: ");
        var input = Console.ReadLine();
        if (input is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(input) ? defaultValue : input.Trim();
    }

    public static bool TryAskScanType(out ScanType scanType)
    {
        scanType = ScanType.Pickup;
        var input = Ask("Scan type (Pickup / InTransit / Delivered / Failed)", nameof(ScanType.Pickup));
        return input is not null
            && Enum.TryParse(input, ignoreCase: true, out scanType)
            && Enum.IsDefined(scanType);
    }

    public static void PrintScans(string title, IReadOnlyList<ShipmentScan> scans)
    {
        Console.WriteLine($"--- {title} ({scans.Count}) ---");
        foreach (var scan in scans)
        {
            Console.WriteLine(
                $"{scan.DocumentId}  {scan.TrackingNumber,-13} {scan.ScanType,-9} {scan.SyncStatus,-7} " +
                $"{scan.ScannedAtUtc:yyyy-MM-dd HH:mm:ss}Z  {scan.CourierId}  {scan.Location}" +
                (scan.Notes is null ? string.Empty : $"  ({scan.Notes})"));
        }
    }
}
