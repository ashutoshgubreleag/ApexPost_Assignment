using System.Globalization;
using ApexPost.CounterTerminal.Application;
using ApexPost.CounterTerminal.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;

namespace ApexPost.CounterTerminal.Ui;

/// <summary>
/// Console front end. Parses commands and calls the application layer; no business rules here.
/// Commands can also be passed on the command line for scripted demos (one-shot mode).
/// </summary>
public sealed class ConsoleUi(TerminalService terminal, SyncWorker sync, INetworkSwitch network, TerminalOptions options, TextWriter output)
{
    public const string Help = """
        Commands
          help                               this list
          categories                         product categories in the local catalogue
          catalog <category>                 products in a category (SQL++ query, sorted by name)
          stock                              local stock (server quantity - unsynced local sales)
          sell <SKU>x<qty> [...] [pay=Cash|Card|MobileWallet]
                                             sell goods; works offline (saved to the outbox first)
          mo <amount> <sender> <recipient> [pay=...]
                                             money order; above the ID threshold it is blocked offline
          status                             online/offline, pending count, last sync, last error
          pending [minutes]                  pending sales older than N minutes (default 0)
          show <localTransactionId>          one sale with its sync status
          today                              today's local sales count and value
          sync                               push the outbox and pull prices/stock now
          offline | online                   simulate pulling / plugging the network cable
          quit
        """;

    /// <summary>Runs one command; returns false when the user asked to quit.</summary>
    public async Task<bool> ExecuteAsync(string line, CancellationToken cancellationToken)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return true;
        }

        try
        {
            switch (parts[0].ToLowerInvariant())
            {
                case "help" or "?":
                    output.WriteLine(Help);
                    break;
                case "categories":
                    output.WriteLine(string.Join(", ", terminal.Categories()));
                    break;
                case "catalog":
                    Catalog(parts.Length > 1 ? string.Join(' ', parts[1..]) : string.Empty);
                    break;
                case "stock":
                    Stock();
                    break;
                case "sell":
                    Sell(parts[1..]);
                    break;
                case "mo":
                    MoneyOrder(parts[1..]);
                    break;
                case "status":
                    Status();
                    break;
                case "pending":
                    Pending(parts.Length > 1 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var minutes) ? minutes : 0);
                    break;
                case "show":
                    Show(parts.Length > 1 ? parts[1] : string.Empty);
                    break;
                case "today":
                    var (count, total) = terminal.TodaysSales();
                    output.WriteLine($"Today: {count} sale(s), {total}");
                    break;
                case "sync":
                    Report(await sync.SyncOnceAsync(cancellationToken).ConfigureAwait(false));
                    break;
                case "offline":
                    network.IsDisconnected = true;
                    output.WriteLine("Network OFF. Sales keep working and queue in the outbox.");
                    Report(await sync.SyncOnceAsync(cancellationToken).ConfigureAwait(false));
                    break;
                case "online":
                    network.IsDisconnected = false;
                    output.WriteLine("Network ON. Syncing...");
                    Report(await sync.SyncOnceAsync(cancellationToken).ConfigureAwait(false));
                    break;
                case "quit" or "exit":
                    return false;
                default:
                    output.WriteLine($"Unknown command '{parts[0]}'. Type help.");
                    break;
            }
        }
        catch (DomainException ex)
        {
            output.WriteLine($"Refused: {ex.Message}");
        }

        return true;
    }

    public void Report(SyncReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        output.WriteLine(report.Succeeded
            ? $"Sync OK: pushed {report.Pushed} (accepted {report.Accepted}, warnings {report.Warnings}, rejected {report.Rejected}); pulled {report.ProductsPulled} product(s), {report.StockPulled} stock row(s)."
            : $"Sync failed, sales kept in the outbox: {report.Error}");
    }

    private void Catalog(string category)
    {
        if (category.Length == 0)
        {
            output.WriteLine("Usage: catalog <category>. Categories: " + string.Join(", ", terminal.Categories()));
            return;
        }

        foreach (var item in terminal.ProductsIn(category))
        {
            output.WriteLine($"  {item.Sku.Value,-12} {item.Name,-32} {item.UnitPrice,10}");
        }
    }

    private void Stock()
    {
        foreach (var s in terminal.Stock())
        {
            var low = s.Available <= s.ReorderThreshold ? "  LOW" : string.Empty;
            output.WriteLine($"  {s.Sku.Value,-12} server {s.ServerQuantity,5}  sold-unsynced {s.LocalSoldSinceSync,4}  available {s.Available,5}{low}");
        }
    }

    private void Sell(string[] args)
    {
        var pay = PaymentArg(args);
        var items = new List<(string Sku, int Quantity)>();
        foreach (var arg in args.Where(a => !a.StartsWith("pay=", StringComparison.OrdinalIgnoreCase)))
        {
            var x = arg.LastIndexOf('x');
            if (x <= 0 || !int.TryParse(arg[(x + 1)..], CultureInfo.InvariantCulture, out var qty))
            {
                output.WriteLine($"'{arg}' should look like SKUx2.");
                return;
            }

            items.Add((arg[..x], qty));
        }

        Print(terminal.Sell(items, pay));
    }

    private void MoneyOrder(string[] args)
    {
        var pay = PaymentArg(args);
        var rest = args.Where(a => !a.StartsWith("pay=", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (rest.Length < 3 || !decimal.TryParse(rest[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            output.WriteLine("Usage: mo <amount> <sender> <recipient> [pay=Cash]");
            return;
        }

        Print(terminal.Sell([], pay, [new PendingMoneyOrder(new Money(amount), rest[1], rest[2])]));
    }

    private void Status()
    {
        var s = terminal.Status();
        output.WriteLine($"Device {options.DeviceId} @ {options.BranchCode} counter {options.CounterNumber}");
        output.WriteLine($"  Link: {(network.IsDisconnected ? "OFFLINE (switched off)" : s.IsOnline ? "online" : "offline (server unreachable)")}");
        output.WriteLine($"  Outbox: {s.PendingCount} pending");
        output.WriteLine($"  Last sync: {s.LastSuccessfulSyncAt?.ToString("u", CultureInfo.InvariantCulture) ?? "never"}");
        if (s.LastError is not null)
        {
            output.WriteLine($"  Last error: {s.LastError} ({s.LastErrorAt?.ToString("u", CultureInfo.InvariantCulture)})");
        }
    }

    private void Pending(int minutes)
    {
        var stuck = terminal.StuckSales(TimeSpan.FromMinutes(minutes));
        output.WriteLine($"{stuck.Count} pending sale(s) older than {minutes} minute(s):");
        foreach (var sale in stuck)
        {
            output.WriteLine($"  {sale.LocalTransactionId:D}  {sale.CapturedAt:u}  {sale.Total,10}  attempts {sale.Attempts}  {sale.LastError}");
        }
    }

    private void Show(string id)
    {
        if (!Guid.TryParse(id, out var guid) || terminal.FindSale(guid) is not { } sale)
        {
            output.WriteLine("No such sale on this terminal.");
            return;
        }

        Print(sale);
        foreach (var warning in sale.Warnings)
        {
            output.WriteLine($"  warning: {warning}");
        }
    }

    private void Print(PendingSale sale)
    {
        output.WriteLine($"Sale {sale.LocalTransactionId:D}  [{sale.Status}]  {sale.PaymentMethod}  total {sale.Total}");
        foreach (var line in sale.Lines)
        {
            output.WriteLine($"  {line.Quantity} x {line.Name} ({line.Sku.Value}) @ {line.UnitPrice} = {line.LineTotal}");
        }

        foreach (var order in sale.MoneyOrders)
        {
            output.WriteLine($"  Money order {order.Amount} from {order.Sender} to {order.Recipient}");
        }
    }

    private static string PaymentArg(string[] args) =>
        args.FirstOrDefault(a => a.StartsWith("pay=", StringComparison.OrdinalIgnoreCase))?[4..] ?? "Cash";
}
