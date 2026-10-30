using ApexPost.CounterTerminal.Application;
using ApexPost.CounterTerminal.Infrastructure.Couchbase;
using ApexPost.CounterTerminal.Infrastructure.Http;
using ApexPost.CounterTerminal.Ui;
using Microsoft.Extensions.Configuration;

// Composition root only: configuration, object graph, lifetime. No business logic here.
// Usage:
//   dotnet run --project ApexPost.CounterTerminal                       (interactive)
//   dotnet run --project ApexPost.CounterTerminal -- --run "sell STA-001x2;status"   (one-shot)
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables("APEXPOST_")
    .AddCommandLine(args)
    .Build();

var options = configuration.GetSection(TerminalOptions.SectionName).Get<TerminalOptions>() ?? new TerminalOptions();
var clock = TimeProvider.System;
var network = new NetworkSwitch(options.ForceOffline);

using var database = new TerminalDatabase(options.DataDirectory);
using var monitor = new TerminalChangeMonitor(database); // disposed before the database (reverse order)
using var http = new HttpClient();

var catalog = new CouchbaseCatalogStore(database);
var stock = new CouchbaseStockStore(database);
var sales = new CouchbasePendingSaleStore(database);
var syncState = new CouchbaseSyncStateStore(database, options);
var api = new SyncApiClient(http, options, network);

var terminal = new TerminalService(catalog, stock, sales, syncState, options, clock);
var worker = new SyncWorker(sales, catalog, stock, syncState, api, options, clock);
var ui = new ConsoleUi(terminal, worker, network, options, Console.Out);

Console.WriteLine($"ApexPost counter terminal {options.DeviceId} ({options.BranchCode}). Data: {Path.GetFullPath(options.DataDirectory)}");
if (string.IsNullOrEmpty(options.DeviceKey))
{
    Console.WriteLine("No device key configured: sync will be refused. Set it with");
    Console.WriteLine("  dotnet user-secrets set \"Terminal:DeviceKey\" \"<key>\" --project ApexPost.CounterTerminal");
}

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

var oneShot = configuration["run"];
if (oneShot is not null)
{
    // Scripted demo: sync once, run the commands in order, exit.
    ui.Report(await worker.SyncOnceAsync(shutdown.Token));
    foreach (var command in oneShot.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        Console.WriteLine($"> {command}");
        await ui.ExecuteAsync(command, shutdown.Token);
    }

    return;
}

monitor.Start();
worker.Completed += (_, report) =>
{
    if (!report.Succeeded || report.Pushed > 0)
    {
        Console.WriteLine();
        ui.Report(report);
        Console.Write("> ");
    }
};
var background = worker.RunLoopAsync(monitor, shutdown.Token);

Console.WriteLine("Type help for commands.");
while (!shutdown.IsCancellationRequested)
{
    Console.Write("> ");
    var line = await Console.In.ReadLineAsync(shutdown.Token);
    if (line is null || !await ui.ExecuteAsync(line, shutdown.Token))
    {
        break;
    }
}

await shutdown.CancelAsync();
try
{
    await background;
}
catch (OperationCanceledException)
{
    // Normal shutdown.
}

public partial class Program;
