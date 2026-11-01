using System.Net;
using System.Net.Http.Json;
using ApexPost.Contracts;
using ApexPost.Models.Domain;
using ApexPost.Tests.Support;

namespace ApexPost.Tests.Integration;

/// <summary>/api/sync: device authentication, branch binding, idempotent pushes, Problem Details, rate limit.</summary>
public sealed class SyncApiTests : IClassFixture<ApexPostWebApplicationFactory>
{
    private readonly ApexPostWebApplicationFactory _factory;

    public SyncApiTests(ApexPostWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Push_without_device_credentials_is_401_problem_details()
    {
        using var client = _factory.CreateBrowser("127.0.0.1");

        using var response = await client.PostAsJsonAsync("api/sync/transactions", Request(Sale()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Push_with_a_wrong_device_key_is_401()
    {
        using var client = Device("TERM-TEST-01", "not-the-key");

        using var response = await client.GetAsync(new Uri("api/sync/branch-snapshot", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Push_records_the_sale_and_a_retry_is_reported_as_duplicate()
    {
        using var client = Device("TERM-TEST-01", ApexPostWebApplicationFactory.TestDeviceKey);
        var sale = Sale();

        using var first = await client.PostAsJsonAsync("api/sync/transactions", Request(sale));
        using var second = await client.PostAsJsonAsync("api/sync/transactions", Request(sale));

        first.EnsureSuccessStatusCode();
        var a = (await first.Content.ReadFromJsonAsync<PushTransactionsResponse>())!.Results.Single();
        var b = (await second.Content.ReadFromJsonAsync<PushTransactionsResponse>())!.Results.Single();
        Assert.Equal("Accepted", a.Outcome);
        Assert.False(a.Duplicate);
        Assert.True(b.Duplicate);
        Assert.Equal(a.ServerTransactionId, b.ServerTransactionId);
        Assert.Single(_factory.Store.All<SyncInboxEntry>(), e => e.LocalTransactionId == sale.LocalTransactionId);
    }

    [Fact]
    public async Task Device_is_bound_to_its_branch_snapshot()
    {
        using var client = Device("TERM-TEST-02", ApexPostWebApplicationFactory.TestDeviceKey + "-2");

        var snapshot = await client.GetFromJsonAsync<BranchSnapshotResponse>("api/sync/branch-snapshot");

        Assert.NotNull(snapshot);
        Assert.Equal("BR-002", snapshot.BranchCode);
        Assert.NotEmpty(snapshot.Products);
        Assert.NotEmpty(snapshot.Stock);
    }

    [Fact]
    public async Task Unsupported_schema_version_is_400_problem_details()
    {
        using var client = Device("TERM-TEST-01", ApexPostWebApplicationFactory.TestDeviceKey);

        using var response = await client.PostAsJsonAsync("api/sync/transactions", new PushTransactionsRequest(99, [Sale()]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Device_requests_beyond_the_limit_get_429()
    {
        await using var factory = ApexPostWebApplicationFactory.With(new() { ["Sync:RequestsPerMinute"] = "2" });
        using var client = factory.CreateBrowser("127.0.0.1");
        client.DefaultRequestHeaders.Add(SyncHeaders.DeviceId, "TERM-TEST-01");
        client.DefaultRequestHeaders.Add(SyncHeaders.DeviceKey, ApexPostWebApplicationFactory.TestDeviceKey);

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var response = await client.GetAsync(new Uri("api/sync/branch-snapshot", UriKind.Relative));
            codes.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, codes[0]);
        Assert.Contains(HttpStatusCode.TooManyRequests, codes);
    }

    private HttpClient Device(string id, string key)
    {
        var client = _factory.CreateBrowser("127.0.0.1");
        client.DefaultRequestHeaders.Add(SyncHeaders.DeviceId, id);
        client.DefaultRequestHeaders.Add(SyncHeaders.DeviceKey, key);
        return client;
    }

    private static PushTransactionsRequest Request(params OfflineSaleDto[] sales) => new(SyncContract.SchemaVersion, sales);

    private static OfflineSaleDto Sale() => new(
        Guid.NewGuid(), 1, 7, new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.Zero), "Cash",
        [new OfflineSaleLineDto("PKG-003", 1, 4.95m)], []);
}
