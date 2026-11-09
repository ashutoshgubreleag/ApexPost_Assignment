using System.Collections.Concurrent;
using System.Net;
using System.Text;
using ApexPost.Courier.Core;
using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Core.Auth;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Offline.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Courier.Core.Tests;

internal sealed record SeenRequest(HttpMethod Method, string Path, string? Authorization, string Body);

/// <summary>Fake network: counts calls, records requests, answers from a script.</summary>
internal sealed class StubHandler(Func<int, HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public ConcurrentQueue<SeenRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var n = Interlocked.Increment(ref _calls);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue(new SeenRequest(request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), body));
        return respond(n, request);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };
}

internal sealed class InMemoryScanRepository : IScanRepository
{
    private readonly Dictionary<Guid, ShipmentScan> _items = [];

    public ShipmentScan Create(ShipmentScan scan)
    {
        var pending = scan with { SyncStatus = SyncStatus.Pending };
        _items[scan.ScanId] = pending;
        return pending;
    }

    public ShipmentScan? Get(Guid scanId) => _items.GetValueOrDefault(scanId);

    public bool Update(ShipmentScan scan)
    {
        if (!_items.ContainsKey(scan.ScanId))
        {
            return false;
        }

        _items[scan.ScanId] = scan;
        return true;
    }

    public bool Delete(Guid scanId) => _items.Remove(scanId);

    public IReadOnlyList<ShipmentScan> ListAll() => [.. _items.Values.OrderBy(s => s.ScannedAtUtc)];

    public IReadOnlyList<ShipmentScan> GetByTrackingNumber(string trackingNumber) =>
        [.. _items.Values.Where(s => s.TrackingNumber == trackingNumber).OrderBy(s => s.ScannedAtUtc)];

    public IReadOnlyList<ShipmentScan> GetPending(int? take = null) =>
        [.. _items.Values.Where(s => s.SyncStatus == SyncStatus.Pending).OrderBy(s => s.ScannedAtUtc).Take(take ?? int.MaxValue)];

    public int CountPending() => _items.Values.Count(s => s.SyncStatus == SyncStatus.Pending);

    public IReadOnlyDictionary<ScanType, int> CountByScanType(string courierId) =>
        Enum.GetValues<ScanType>().ToDictionary(t => t, t => _items.Values.Count(s => s.CourierId == courierId && s.ScanType == t));
}

internal sealed class FakeConnectivity(bool online = true) : INetworkStatus
{
    public bool IsOnline { get; private set; } = online;

    public event EventHandler<bool>? OnlineChanged;

    public void Set(bool online)
    {
        IsOnline = online;
        OnlineChanged?.Invoke(this, online);
    }
}

/// <summary>The real DI wiring (client, auth handler, Polly) on top of a fake network.</summary>
internal sealed class Host : IDisposable
{
    private readonly ServiceProvider _provider;

    public Host(StubHandler handler, ResilienceSettings? settings = null, string? token = "t0ken", bool online = true)
    {
        Handler = handler;
        Repo = new InMemoryScanRepository();
        Tokens = new InMemoryTokenStore();
        if (token is not null)
        {
            Tokens.SetAsync(token).GetAwaiter().GetResult();
        }

        Connectivity = new FakeConnectivity(online);
        var services = new ServiceCollection();
        services.AddSingleton<IScanRepository>(Repo);
        services.AddSingleton<ITokenStore>(Tokens);
        services.AddSingleton<INetworkStatus>(Connectivity);
        services.AddApexCourierCore(o =>
        {
            o.BaseAddress = new Uri("https://api.test/");
            o.Resilience = settings ?? Fast();
        }).ConfigurePrimaryHttpMessageHandler(() => handler);
        _provider = services.BuildServiceProvider();
    }

    public StubHandler Handler { get; }

    public InMemoryScanRepository Repo { get; }

    public InMemoryTokenStore Tokens { get; }

    public FakeConnectivity Connectivity { get; }

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>Real policy shape, tiny delays so tests are quick. MinimumThroughput is high so the circuit stays closed unless a test lowers it.</summary>
    public static ResilienceSettings Fast() => new()
    {
        MaxRetryAttempts = 2,
        RetryBaseDelay = TimeSpan.FromMilliseconds(1),
        CircuitMinimumThroughput = 100,
        AttemptTimeout = TimeSpan.FromSeconds(5),
        TotalTimeout = TimeSpan.FromSeconds(20),
    };

    public ShipmentScan AddScan(ScanType type = ScanType.Pickup, string tracking = "APX-10000001", string? notes = null) =>
        Repo.Create(ShipmentScan.CreateNew(tracking, type, "courier-1", "Melbourne Depot", DateTimeOffset.UtcNow.AddMinutes(-5), notes));

    public void Dispose() => _provider.Dispose();
}
