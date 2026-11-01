using System.Net;
using ApexPost.Tests.Support;

namespace ApexPost.Tests.Integration;

/// <summary>/integration/parcel-status: API key, IP restriction, CORS, rate limit and timeouts.</summary>
public sealed class IntegrationEndpointTests : IClassFixture<ApexPostWebApplicationFactory>
{
    private const string KnownTracking = "/integration/parcel-status/";
    private readonly ApexPostWebApplicationFactory _factory;

    public IntegrationEndpointTests(ApexPostWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Returns401_WithoutApiKey()
    {
        using var client = _factory.CreateBrowser("127.0.0.1");

        using var response = await client.GetAsync(new Uri(KnownTracking + "APX000001", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Returns401_WithWrongApiKey()
    {
        using var client = _factory.CreateBrowser("127.0.0.1");
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        using var response = await client.GetAsync(new Uri(KnownTracking + "APX000001", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns403_FromBlockedIp_EvenWithAValidKey()
    {
        using var client = _factory.CreateBrowser("203.0.113.50");
        client.DefaultRequestHeaders.Add("X-Api-Key", ApexPostWebApplicationFactory.TestApiKey);

        using var response = await client.GetAsync(new Uri(KnownTracking + "APX000001", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SpoofedForwardedForHeader_DoesNotBypassIpRestriction()
    {
        using var client = _factory.CreateBrowser("203.0.113.50");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        client.DefaultRequestHeaders.Add("X-Api-Key", ApexPostWebApplicationFactory.TestApiKey);

        using var response = await client.GetAsync(new Uri(KnownTracking + "APX000001", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminArea_IsIpRestrictedToo()
    {
        using var client = _factory.CreateBrowser("198.51.100.7");

        using var response = await client.GetAsync(new Uri("/Admin/Audit", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ValidKey_ReturnsTrackingJson_AndCorsAllowsOnlyThePartnerOrigin()
    {
        var tracking = await FirstLodgedTrackingNumberAsync();
        using var client = _factory.CreateBrowser("127.0.0.1");
        client.DefaultRequestHeaders.Add("X-Api-Key", ApexPostWebApplicationFactory.TestApiKey);

        using var allowed = new HttpRequestMessage(HttpMethod.Get, KnownTracking + tracking);
        allowed.Headers.Add("Origin", "https://track.swiftcourier.example");
        using var ok = await client.SendAsync(allowed);

        using var denied = new HttpRequestMessage(HttpMethod.Get, KnownTracking + tracking);
        denied.Headers.Add("Origin", "https://evil.example");
        using var other = await client.SendAsync(denied);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains(tracking, await ok.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("https://track.swiftcourier.example", ok.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(other.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task UnknownTrackingNumber_Returns404()
    {
        using var client = _factory.CreateBrowser("127.0.0.1");
        client.DefaultRequestHeaders.Add("X-Api-Key", ApexPostWebApplicationFactory.TestApiKey);

        using var response = await client.GetAsync(new Uri(KnownTracking + "APX000000000", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SlowCourier_TimesOutAs503()
    {
        using var client = _factory.CreateBrowser("127.0.0.1");
        client.DefaultRequestHeaders.Add("X-Api-Key", ApexPostWebApplicationFactory.TestApiKey);

        using var response = await client.GetAsync(new Uri(KnownTracking + "APX261020999", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private async Task<string> FirstLodgedTrackingNumberAsync()
    {
        _ = _factory.Server; // ensure the host started
        return await TestData.LodgedShipmentAsync(_factory.Store);
    }
}

public sealed class IntegrationNotConfiguredTests : IDisposable
{
    private readonly ApexPostWebApplicationFactory _factory = ApexPostWebApplicationFactory.With(new() { ["Integration:Partners:swiftcourier"] = "" });

    [Fact]
    public async Task Returns500_WhenNoKeyIsConfigured()
    {
        using var client = _factory.CreateBrowser("127.0.0.1");
        client.DefaultRequestHeaders.Add("X-Api-Key", "anything");

        using var response = await client.GetAsync(new Uri("/integration/parcel-status/APX1", UriKind.Relative));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}

public sealed class PartnerRateLimitTests : IDisposable
{
    private readonly ApexPostWebApplicationFactory _factory = ApexPostWebApplicationFactory.With(new()
    {
        ["RateLimiting:PartnerPermitLimit"] = "3",
        ["Integration:Partners:otherpartner"] = "other-partner-key-987654321",
    });

    [Fact]
    public async Task EachPartner_HasItsOwnBudget()
    {
        using var first = _factory.CreateBrowser("127.0.0.1");
        first.DefaultRequestHeaders.Add("X-Api-Key", ApexPostWebApplicationFactory.TestApiKey);
        using var second = _factory.CreateBrowser("127.0.0.1");
        second.DefaultRequestHeaders.Add("X-Api-Key", "other-partner-key-987654321");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var r = await first.GetAsync(new Uri("/integration/parcel-status/APX000000000", UriKind.Relative));
            statuses.Add(r.StatusCode);
        }

        using var otherPartner = await second.GetAsync(new Uri("/integration/parcel-status/APX000000000", UriKind.Relative));

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        Assert.Equal(HttpStatusCode.NotFound, otherPartner.StatusCode); // not throttled by the first partner
    }

    public void Dispose() => _factory.Dispose();
}
