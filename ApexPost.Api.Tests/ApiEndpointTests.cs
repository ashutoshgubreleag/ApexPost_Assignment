using System.Net;
using System.Text;
using System.Text.Json;
using ApexPost.Api.Tests.Support;
using ApexPost.Services.Abstractions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ApexPost.Api.Tests;

/// <summary>
/// The real HTTP pipeline against the in-memory store: status codes, Location header, ProblemDetails,
/// validation and the global exception handler.
/// </summary>
public sealed class ApiEndpointTests : IClassFixture<ApiFactory>
{
    private const string ProblemJson = "application/problem+json";
    private static int _counter = 50_000_000;

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ApiEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NextTn() => $"APX-{Interlocked.Increment(ref _counter)}";

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static string CreateBody(string trackingNumber, string city = "Melbourne") => $$"""
        {"trackingNumber":"{{trackingNumber}}","recipientName":"Ada Lovelace","recipientPhone":"+61 400 000 111","address":"12 Example Street","city":"{{city}}","weightKg":2.5}
        """;

    private static string UpdateBody(string city = "Sydney") => $$"""
        {"recipientName":"Grace Hopper","recipientPhone":"0400 000 333","address":"9 New Lane","city":"{{city}}","weightKg":4}
        """;

    private static string ScanBody(Guid scanId, string scanType, string? signature = null, DateTimeOffset? at = null) =>
        JsonSerializer.Serialize(new
        {
            scans = new[]
            {
                new
                {
                    scanId,
                    scanType,
                    location = "Melbourne Depot",
                    scannedAtUtc = at ?? DateTimeOffset.UtcNow.AddHours(-1),
                    recipientSignatureName = signature,
                },
            },
        });

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private async Task<string> CreateShipmentAsync(string city = "Melbourne")
    {
        var tn = NextTn();
        var response = await _client.PostAsync("/api/shipments", Json(CreateBody(tn, city)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return tn;
    }

    private Task<HttpResponseMessage> PatchStatusAsync(string tn, string status) =>
        _client.PatchAsync($"/api/shipments/{tn}/status", Json($$"""{"status":"{{status}}"}"""));

    // ---- 201 / 200 ----

    [Fact]
    public async Task Post_ValidShipment_Returns201WithLocationAndBody()
    {
        var tn = NextTn();

        var response = await _client.PostAsync("/api/shipments", Json(CreateBody(tn)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith($"/api/shipments/{tn}", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(tn, body.RootElement.GetProperty("trackingNumber").GetString());
        Assert.Equal("Created", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("Melbourne", body.RootElement.GetProperty("city").GetString());
    }

    [Fact]
    public async Task Get_LocationHeaderOfACreatedShipment_ReturnsIt()
    {
        var tn = NextTn();
        var created = await _client.PostAsync("/api/shipments", Json(CreateBody(tn)));

        var response = await _client.GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(tn, body.RootElement.GetProperty("trackingNumber").GetString());
    }

    [Fact]
    public async Task Put_ReplacesDetails_Returns200()
    {
        var tn = await CreateShipmentAsync();

        var response = await _client.PutAsync($"/api/shipments/{tn}", Json(UpdateBody()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("Grace Hopper", body.RootElement.GetProperty("recipientName").GetString());
        Assert.Equal("Sydney", body.RootElement.GetProperty("city").GetString());
    }

    [Fact]
    public async Task Patch_NextStatus_Returns200WithNewStatus()
    {
        var tn = await CreateShipmentAsync();

        var response = await PatchStatusAsync(tn, "PickedUp");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("PickedUp", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task List_CapsPageSizeAtFiftyAndFiltersByCity()
    {
        var city = $"Cityville{NextTn()}";
        await CreateShipmentAsync(city);

        var response = await _client.GetAsync($"/api/shipments?pageSize=500&city={city}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(50, body.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("items").GetArrayLength());
    }

    // ---- 400 ----

    [Fact]
    public async Task Post_InvalidBody_Returns400ValidationProblemDetails()
    {
        var response = await _client.PostAsync("/api/shipments", Json("""{"trackingNumber":"BAD","recipientName":"","weightKg":100}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var body = await ReadJsonAsync(response);
        var errors = body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains(errors, n => string.Equals(n, "trackingNumber", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, n => string.Equals(n, "weightKg", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, n => string.Equals(n, "city", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(400, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Patch_UnknownStatusName_Returns400()
    {
        var tn = await CreateShipmentAsync();

        var response = await PatchStatusAsync(tn, "Teleported");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_UnknownStatusFilter_Returns400() =>
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/shipments?status=Teleported")).StatusCode);

    [Fact]
    public async Task Scans_FutureScanTime_Returns400()
    {
        var tn = await CreateShipmentAsync();

        var response = await _client.PostAsync($"/api/shipments/{tn}/scans", Json(ScanBody(Guid.NewGuid(), "PickedUp", at: DateTimeOffset.UtcNow.AddDays(2))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("future", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scans_DeliveredWithoutSignature_Returns400()
    {
        var tn = await CreateShipmentAsync();

        var response = await _client.PostAsync($"/api/shipments/{tn}/scans", Json(ScanBody(Guid.NewGuid(), "Delivered")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("signature", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    // ---- 404 ----

    [Fact]
    public async Task Get_UnknownTrackingNumber_Returns404ProblemDetails()
    {
        var response = await _client.GetAsync("/api/shipments/APX-99999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("NOT_FOUND", body.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Get_MalformedTrackingNumber_DoesNotMatchTheRoute() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/shipments/not-a-number")).StatusCode);

    [Fact]
    public async Task EveryEndpointWithATrackingNumber_Returns404WhenUnknown()
    {
        const string unknown = "/api/shipments/APX-99999998";

        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsync(unknown, Json(UpdateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PatchAsync($"{unknown}/status", Json("""{"status":"PickedUp"}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync(unknown)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{unknown}/scans")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"{unknown}/scans", Json(ScanBody(Guid.NewGuid(), "PickedUp")))).StatusCode);
    }

    // ---- 409 ----

    [Fact]
    public async Task Post_DuplicateTrackingNumber_Returns409ProblemDetails()
    {
        var tn = await CreateShipmentAsync();

        var response = await _client.PostAsync("/api/shipments", Json(CreateBody(tn)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("DUPLICATE_TRACKING_NUMBER", body.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Patch_SkippingAStatus_Returns409()
    {
        var tn = await CreateShipmentAsync();

        var response = await PatchStatusAsync(tn, "Delivered");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("INVALID_STATUS_TRANSITION", body.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Put_AfterPickup_Returns409()
    {
        var tn = await CreateShipmentAsync();
        await PatchStatusAsync(tn, "PickedUp");

        var response = await _client.PutAsync($"/api/shipments/{tn}", Json(UpdateBody()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- 204 and the delete rule ----

    [Fact]
    public async Task Delete_CreatedShipment_Returns204ThenItIsGone()
    {
        var tn = await CreateShipmentAsync();

        var deleted = await _client.DeleteAsync($"/api/shipments/{tn}");
        var after = await _client.GetAsync($"/api/shipments/{tn}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_ShipmentPastCreated_Returns409()
    {
        var tn = await CreateShipmentAsync();
        await PatchStatusAsync(tn, "PickedUp");

        var response = await _client.DeleteAsync($"/api/shipments/{tn}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/shipments/{tn}")).StatusCode);
    }

    // ---- 415 ----

    [Theory]
    [InlineData("POST", "/api/shipments")]
    [InlineData("PUT", "/api/shipments/APX-99999997")]
    [InlineData("PATCH", "/api/shipments/APX-99999997/status")]
    [InlineData("POST", "/api/shipments/APX-99999997/scans")]
    public async Task WriteEndpoints_RejectANonJsonBodyWith415(string method, string url)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = new StringContent("tracking=APX-1", Encoding.UTF8, "text/plain"),
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    // ---- scans ----

    [Fact]
    public async Task Scans_UploadingTheSameBatchTwice_IsIdempotent()
    {
        var tn = await CreateShipmentAsync();
        var scanId = Guid.NewGuid();
        var body = ScanBody(scanId, "PickedUp");

        var first = await _client.PostAsync($"/api/shipments/{tn}/scans", Json(body));
        var second = await _client.PostAsync($"/api/shipments/{tn}/scans", Json(body));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var firstBody = await ReadJsonAsync(first);
        using var secondBody = await ReadJsonAsync(second);
        Assert.Equal(1, firstBody.RootElement.GetProperty("accepted").GetInt32());
        Assert.Equal(0, firstBody.RootElement.GetProperty("duplicatesSkipped").GetInt32());
        Assert.Equal(0, secondBody.RootElement.GetProperty("accepted").GetInt32());
        Assert.Equal(1, secondBody.RootElement.GetProperty("duplicatesSkipped").GetInt32());
        Assert.Equal(0, secondBody.RootElement.GetProperty("rejected").GetArrayLength());

        using var history = await ReadJsonAsync(await _client.GetAsync($"/api/shipments/{tn}/scans"));
        Assert.Equal(1, history.RootElement.GetArrayLength());
        Assert.Equal(scanId, history.RootElement[0].GetProperty("scanId").GetGuid());
        using var shipment = await ReadJsonAsync(await _client.GetAsync($"/api/shipments/{tn}"));
        Assert.Equal("PickedUp", shipment.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Scans_ScanIdUsedOnAnotherShipment_IsListedAsRejected()
    {
        var first = await CreateShipmentAsync();
        var second = await CreateShipmentAsync();
        var scanId = Guid.NewGuid();
        await _client.PostAsync($"/api/shipments/{first}/scans", Json(ScanBody(scanId, "PickedUp")));

        var response = await _client.PostAsync($"/api/shipments/{second}/scans", Json(ScanBody(scanId, "PickedUp")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(0, body.RootElement.GetProperty("accepted").GetInt32());
        var rejected = body.RootElement.GetProperty("rejected");
        Assert.Equal(1, rejected.GetArrayLength());
        Assert.Equal(scanId, rejected[0].GetProperty("scanId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(rejected[0].GetProperty("reason").GetString()));
    }

    // ---- 500 and the global handler ----

    [Fact]
    public async Task UnexpectedException_Returns500ProblemDetailsWithoutLeakingDetails()
    {
        using var failing = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITrackingQueries>();
            services.AddScoped<ITrackingQueries, ThrowingQueries>();
        }));
        using var client = failing.CreateClient();

        var response = await client.GetAsync("/api/shipments/APX-10000001");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ApexPost", text, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(text);
        Assert.Equal(500, body.RootElement.GetProperty("status").GetInt32());
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));
    }
}
