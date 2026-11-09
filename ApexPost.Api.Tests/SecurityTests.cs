using System.Net;
using System.Text;
using System.Text.Json;
using ApexPost.Api.Security;
using ApexPost.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace ApexPost.Api.Tests;

/// <summary>
/// Part D: JWT bearer authentication (401), role and ownership authorization (403), the partner API key route,
/// the Development-only token endpoint and the security documentation in the OpenAPI documents.
/// </summary>
public sealed class SecurityTests : IClassFixture<ApiFactory>
{
    private const string ProblemJson = "application/problem+json";
    private static int _counter = 60_000_000;

    private readonly ApiFactory _factory;

    public SecurityTests(ApiFactory factory) => _factory = factory;

    private HttpClient Anonymous() => _factory.CreateClient();

    private HttpClient As(string role, string courierId = "C-1001") => _factory.CreateClient().SignedIn(_factory, role, courierId);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static string NextTn() => $"APX-{Interlocked.Increment(ref _counter)}";

    private static string CreateBody(string tn) => $$"""
        {"trackingNumber":"{{tn}}","recipientName":"Ada Lovelace","recipientPhone":"+61 400 000 111","address":"12 Example Street","city":"Melbourne","weightKg":2.5}
        """;

    private static string ScanBody(string? courierId) => JsonSerializer.Serialize(new
    {
        scans = new[]
        {
            new
            {
                scanId = Guid.NewGuid(),
                scanType = "PickedUp",
                location = "Melbourne Depot",
                scannedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                courierId,
            },
        },
    });

    private async Task<string> CreateShipmentAsync()
    {
        var tn = NextTn();
        using var courier = As(ApiRoles.Courier);
        var response = await courier.PostAsync("/api/v2/shipments", Json(CreateBody(tn)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return tn;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("traceId").GetString()));
    }

    // ---- 401 ----

    [Fact]
    public async Task NoToken_Returns401ProblemDetailsWithBearerChallenge()
    {
        using var client = Anonymous();

        var response = await client.GetAsync("/api/v2/shipments");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Contains("Bearer", string.Join(",", response.Headers.WwwAuthenticate.Select(h => h.Scheme)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/v1/shipments")]
    [InlineData("/api/v2/shipments")]
    [InlineData("/api/v2/shipments/APX-10000001/scans")]
    public async Task EveryApiRouteRequiresAToken(string url)
    {
        using var client = Anonymous();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task GarbageToken_Returns401()
    {
        using var client = Anonymous().WithToken("this-is-not-a-jwt");

        await AssertProblemAsync(await client.GetAsync("/api/v2/shipments"), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        var now = DateTime.UtcNow;
        var expired = AuthTestExtensions.CreateCustomToken(ApiRoles.Courier, now.AddHours(-2), now.AddHours(-1));
        using var client = Anonymous().WithToken(expired);

        await AssertProblemAsync(await client.GetAsync("/api/v2/shipments"), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenSignedWithAnotherKey_Returns401()
    {
        var now = DateTime.UtcNow;
        var forged = AuthTestExtensions.CreateCustomToken(ApiRoles.Admin, now, now.AddHours(1), signingKey: "another-key-that-is-long-enough-0123456789");
        using var client = Anonymous().WithToken(forged);

        await AssertProblemAsync(await client.GetAsync("/api/v2/shipments"), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenForAnotherAudience_Returns401()
    {
        var now = DateTime.UtcNow;
        var wrongAudience = AuthTestExtensions.CreateCustomToken(ApiRoles.Courier, now, now.AddHours(1), audience: "some-other-api");
        using var client = Anonymous().WithToken(wrongAudience);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v2/shipments")).StatusCode);
    }

    // ---- success ----

    [Theory]
    [InlineData(ApiRoles.Courier)]
    [InlineData(ApiRoles.Admin)]
    public async Task ValidToken_AnyRole_CanReadShipments(string role)
    {
        using var client = As(role);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v2/shipments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/shipments")).StatusCode);
    }

    [Fact]
    public async Task Courier_CanUploadScansUnderTheirOwnCourierId()
    {
        var tn = await CreateShipmentAsync();
        using var courier = As(ApiRoles.Courier, "C-1001");

        var explicitOwn = await courier.PostAsync($"/api/v2/shipments/{tn}/scans", Json(ScanBody("C-1001")));
        var omitted = await courier.PostAsync($"/api/v2/shipments/{tn}/scans", Json(ScanBody(null)));

        Assert.Equal(HttpStatusCode.OK, explicitOwn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, omitted.StatusCode);
    }

    [Fact]
    public async Task Admin_CanDeleteACreatedShipment()
    {
        var tn = await CreateShipmentAsync();
        using var admin = As(ApiRoles.Admin, "A-1");

        var response = await admin.DeleteAsync($"/api/v2/shipments/{tn}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---- 403 ----

    [Fact]
    public async Task Courier_CannotDeleteAShipment_Returns403ProblemDetails()
    {
        var tn = await CreateShipmentAsync();
        using var courier = As(ApiRoles.Courier);

        var response = await courier.DeleteAsync($"/api/v2/shipments/{tn}");

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
        using var stillThere = As(ApiRoles.Courier);
        Assert.Equal(HttpStatusCode.OK, (await stillThere.GetAsync($"/api/v2/shipments/{tn}")).StatusCode);
    }

    [Fact]
    public async Task Admin_CannotUploadScans_Returns403()
    {
        var tn = await CreateShipmentAsync();
        using var admin = As(ApiRoles.Admin, "A-1");

        var response = await admin.PostAsync($"/api/v2/shipments/{tn}/scans", Json(ScanBody(null)));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_CannotChangeStatus_Returns403()
    {
        var tn = await CreateShipmentAsync();
        using var admin = As(ApiRoles.Admin, "A-1");

        var response = await admin.PatchAsync($"/api/v2/shipments/{tn}/status", Json("""{"status":"PickedUp"}"""));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Courier_UploadingScansForAnotherCourier_Returns403AndStoresNothing()
    {
        var tn = await CreateShipmentAsync();
        using var courier = As(ApiRoles.Courier, "C-1001");

        var response = await courier.PostAsync($"/api/v2/shipments/{tn}/scans", Json(ScanBody("C-9999")));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("COURIER_MISMATCH", body.RootElement.GetProperty("errorCode").GetString());
        var history = await courier.GetAsync($"/api/v2/shipments/{tn}/scans");
        Assert.Equal("[]", await history.Content.ReadAsStringAsync());
    }

    // ---- partner API key ----

    [Fact]
    public async Task Partner_MissingKey_Returns401ProblemDetails()
    {
        using var client = Anonymous();

        await AssertProblemAsync(await client.GetAsync("/api/v2/partner/shipments/APX-10000001"), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Partner_InvalidKey_Returns401ProblemDetails()
    {
        using var client = Anonymous();
        client.DefaultRequestHeaders.Add("X-Api-Key", "not-a-valid-partner-key");

        await AssertProblemAsync(await client.GetAsync("/api/v2/partner/shipments/APX-10000001"), HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(ApiFactory.TestPartnerKey)]
    [InlineData("test-partner-key-fictional-0002")]
    public async Task Partner_ValidKey_ReturnsTheV2ShipmentWithoutAJwt(string key)
    {
        var tn = await CreateShipmentAsync();
        using var client = Anonymous();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);

        var response = await client.GetAsync($"/api/v2/partner/shipments/{tn}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(tn, body.RootElement.GetProperty("trackingNumber").GetString());
        Assert.Equal("Ada Lovelace", body.RootElement.GetProperty("recipient").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Partner_ValidKey_UnknownTrackingNumber_Returns404()
    {
        using var client = Anonymous();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.TestPartnerKey);

        await AssertProblemAsync(await client.GetAsync("/api/v2/partner/shipments/APX-99999999"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Partner_NoKeysConfigured_Returns500NeverFailsOpen()
    {
        using var noKeys = _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Partner:ApiKeys:0"] = string.Empty,
                ["Partner:ApiKeys:1"] = string.Empty,
            })));
        using var client = noKeys.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.TestPartnerKey);

        await AssertProblemAsync(await client.GetAsync("/api/v2/partner/shipments/APX-10000001"), HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task PartnerKey_DoesNotOpenTheJwtProtectedRoutes()
    {
        using var client = Anonymous();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.TestPartnerKey);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v2/shipments")).StatusCode);
    }

    // ---- /dev/token ----

    [Fact]
    public async Task DevToken_IsNotMappedOutsideDevelopment_Returns404()
    {
        using var client = Anonymous();

        var response = await client.PostAsync("/dev/token", Json("""{"courierId":"C-1001","role":"Courier"}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DevToken_InDevelopment_IssuesATokenTheApiAccepts()
    {
        using var development = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = development.CreateClient();

        var response = await client.PostAsync("/dev/token", Json("""{"courierId":"C-1001","role":"Courier"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrEmpty(token));
        Assert.True(body.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        client.WithToken(token!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v2/shipments")).StatusCode);
    }

    [Theory]
    [InlineData("""{"courierId":"C-1001","role":"Manager"}""")]
    [InlineData("""{"courierId":"","role":"Courier"}""")]
    [InlineData("""{"role":"Admin"}""")]
    public async Task DevToken_InvalidRequest_Returns400ProblemDetails(string body)
    {
        using var development = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = development.CreateClient();

        var response = await client.PostAsync("/dev/token", Json(body));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    // ---- OpenAPI ----

    [Fact]
    public async Task OpenApiV2_DocumentsTheBearerSchemePartnerKeyAndPartnerRoute_V1HasNoPartnerRoute()
    {
        using var development = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = development.CreateClient();

        using var v2 = JsonDocument.Parse(await client.GetStringAsync("/openapi/v2.json"));
        using var v1 = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        var schemes = v2.RootElement.GetProperty("components").GetProperty("securitySchemes");
        Assert.Equal("bearer", schemes.GetProperty("Bearer").GetProperty("scheme").GetString());
        Assert.Equal("X-Api-Key", schemes.GetProperty("PartnerApiKey").GetProperty("name").GetString());
        Assert.True(v2.RootElement.GetProperty("paths").TryGetProperty("/api/v2/partner/shipments/{trackingNumber}", out _));
        Assert.DoesNotContain(v1.RootElement.GetProperty("paths").EnumerateObject(), p => p.Name.Contains("partner", StringComparison.Ordinal));
    }
}
