using System.Net;
using System.Text;
using System.Text.Json;
using ApexPost.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;

namespace ApexPost.Api.Tests;

/// <summary>
/// Part C: v1 (deprecated, flat, numeric status) and v2 (nested recipient, latest scan, string status) side by side,
/// version headers, and the version-specific OpenAPI documents.
/// </summary>
public sealed class ApiVersioningTests : IClassFixture<ApiFactory>
{
    private static int _counter = 60_000_000;

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ApiVersioningTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private async Task<string> CreateViaV1Async()
    {
        var tn = $"APX-{Interlocked.Increment(ref _counter)}";
        var response = await _client.PostAsync("/api/v1/shipments", Json($$"""
            {"trackingNumber":"{{tn}}","recipientName":"Ada Lovelace","recipientPhone":"+61 400 000 111","address":"12 Example Street","city":"Melbourne","weightKg":2.5}
            """));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return tn;
    }

    [Fact]
    public async Task SameShipment_V1IsFlatWithNumericStatus_V2IsNestedWithStringStatus()
    {
        var tn = await CreateViaV1Async();

        using var v1 = await ReadJsonAsync(await _client.GetAsync($"/api/v1/shipments/{tn}"));
        using var v2 = await ReadJsonAsync(await _client.GetAsync($"/api/v2/shipments/{tn}"));

        Assert.Equal(JsonValueKind.Number, v1.RootElement.GetProperty("status").ValueKind);
        Assert.Equal("Ada Lovelace", v1.RootElement.GetProperty("recipientName").GetString());
        Assert.False(v1.RootElement.TryGetProperty("recipient", out _));
        Assert.False(v1.RootElement.TryGetProperty("latestScan", out _));

        Assert.Equal(JsonValueKind.String, v2.RootElement.GetProperty("status").ValueKind);
        Assert.Equal("Created", v2.RootElement.GetProperty("status").GetString());
        var recipient = v2.RootElement.GetProperty("recipient");
        Assert.Equal("Ada Lovelace", recipient.GetProperty("name").GetString());
        Assert.Equal("Melbourne", recipient.GetProperty("city").GetString());
        Assert.False(v2.RootElement.TryGetProperty("recipientName", out _));
        Assert.Equal(JsonValueKind.Null, v2.RootElement.GetProperty("latestScan").ValueKind);
    }

    [Fact]
    public async Task V2_EmbedsTheLatestScan()
    {
        var tn = await CreateViaV1Async();
        var scan = JsonSerializer.Serialize(new
        {
            scans = new[]
            {
                new { scanId = Guid.NewGuid(), scanType = "PickedUp", location = "Melbourne Depot", scannedAtUtc = DateTimeOffset.UtcNow.AddHours(-1) },
            },
        });
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/v2/shipments/{tn}/scans", Json(scan))).StatusCode);

        using var v2 = await ReadJsonAsync(await _client.GetAsync($"/api/v2/shipments/{tn}"));

        var latest = v2.RootElement.GetProperty("latestScan");
        Assert.Equal("PickedUp", latest.GetProperty("scanType").GetString());
        Assert.Equal("Melbourne Depot", latest.GetProperty("location").GetString());
        Assert.Equal("PickedUp", v2.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task V2_Create_ReturnsLocationUnderV2()
    {
        var tn = $"APX-{Interlocked.Increment(ref _counter)}";

        var response = await _client.PostAsync("/api/v2/shipments", Json($$"""
            {"trackingNumber":"{{tn}}","recipientName":"Ada Lovelace","recipientPhone":"+61 400 000 111","address":"12 Example Street","city":"Melbourne","weightKg":2.5}
            """));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/api/v2/shipments/{tn}", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScanContract_IsIdenticalInBothVersions()
    {
        var tn = await CreateViaV1Async();
        var scanId = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new
        {
            scans = new[] { new { scanId, scanType = "Arrived", location = "Depot", scannedAtUtc = DateTimeOffset.UtcNow.AddHours(-2) } },
        });

        var first = await _client.PostAsync($"/api/v1/shipments/{tn}/scans", Json(body));
        var second = await _client.PostAsync($"/api/v2/shipments/{tn}/scans", Json(body));

        using var firstBody = await ReadJsonAsync(first);
        using var secondBody = await ReadJsonAsync(second);
        Assert.Equal(1, firstBody.RootElement.GetProperty("accepted").GetInt32());
        Assert.Equal(1, secondBody.RootElement.GetProperty("duplicatesSkipped").GetInt32());
    }

    [Fact]
    public async Task UnversionedRoute_IsNotFound() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/shipments")).StatusCode);

    [Fact]
    public async Task UnknownVersion_IsRejected()
    {
        var response = await _client.GetAsync("/api/v9/shipments");

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, $"was {(int)response.StatusCode}");
    }

    [Fact]
    public async Task V2Response_ReportsSupportedAndDeprecatedVersions()
    {
        var response = await _client.GetAsync("/api/v2/shipments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("2.0", string.Join(",", response.Headers.GetValues("api-supported-versions")), StringComparison.Ordinal);
        Assert.Contains("1.0", string.Join(",", response.Headers.GetValues("api-deprecated-versions")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task V1Response_IsFlaggedDeprecated()
    {
        var response = await _client.GetAsync("/api/v1/shipments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("1.0", string.Join(",", response.Headers.GetValues("api-deprecated-versions")), StringComparison.Ordinal);
    }

    // ---- OpenAPI documents (Development only, so these tests host the API as Development) ----

    private HttpClient DevelopmentClient() =>
        _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();

    [Fact]
    public async Task OpenApiDocuments_ExistPerVersionWithTheRightPaths()
    {
        using var client = DevelopmentClient();

        using var v1 = await ReadJsonAsync(await client.GetAsync("/openapi/v1.json"));
        using var v2 = await ReadJsonAsync(await client.GetAsync("/openapi/v2.json"));

        var v1Paths = v1.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        var v2Paths = v2.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.NotEmpty(v1Paths);
        Assert.NotEmpty(v2Paths);
        Assert.All(v1Paths, p => Assert.StartsWith("/api/v1/", p, StringComparison.Ordinal));
        Assert.All(v2Paths, p => Assert.StartsWith("/api/v2/", p, StringComparison.Ordinal));
        Assert.StartsWith("3.0", v2.RootElement.GetProperty("openapi").GetString(), StringComparison.Ordinal);
        Assert.Equal("ApexPost Logistics API", v2.RootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("2.0", v2.RootElement.GetProperty("info").GetProperty("version").GetString());
        Assert.Equal("1.0", v1.RootElement.GetProperty("info").GetProperty("version").GetString());
    }

    [Fact]
    public async Task OpenApiV1_MarksEveryOperationDeprecated_V2DoesNot()
    {
        using var client = DevelopmentClient();

        using var v1 = await ReadJsonAsync(await client.GetAsync("/openapi/v1.json"));
        using var v2 = await ReadJsonAsync(await client.GetAsync("/openapi/v2.json"));

        var v1Operations = Operations(v1).ToList();
        Assert.NotEmpty(v1Operations);
        Assert.All(v1Operations, op => Assert.True(op.TryGetProperty("deprecated", out var d) && d.GetBoolean()));
        Assert.All(Operations(v2), op => Assert.False(op.TryGetProperty("deprecated", out var d) && d.GetBoolean()));
    }

    [Fact]
    public async Task OpenApiV2_UsesStableOperationIdsAndStringEnums()
    {
        using var client = DevelopmentClient();

        var text = await (await client.GetAsync("/openapi/v2.json")).Content.ReadAsStringAsync();
        using var v2 = JsonDocument.Parse(text);

        var ids = Operations(v2).Select(op => op.GetProperty("operationId").GetString()).ToList();
        var expected = new[] { "ListShipments", "GetShipment", "CreateShipment", "UpdateShipment", "UpdateShipmentStatus", "DeleteShipment", "UploadScans", "GetScans" };
        Assert.Equal(expected.Order(StringComparer.Ordinal), ids.Order(StringComparer.Ordinal));
        Assert.Contains("\"OutForDelivery\"", text, StringComparison.Ordinal); // status is documented as a string enum
        Assert.Contains("latestScan", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApiV2_DocumentsTheErrorResponses()
    {
        using var client = DevelopmentClient();

        using var v2 = await ReadJsonAsync(await client.GetAsync("/openapi/v2.json"));

        var create = v2.RootElement.GetProperty("paths").GetProperty("/api/v2/shipments").GetProperty("post").GetProperty("responses");
        foreach (var code in new[] { "201", "400", "409", "415", "500" })
        {
            Assert.True(create.TryGetProperty(code, out _), $"POST /api/v2/shipments should document {code}");
        }
    }

    [Fact]
    public async Task SwaggerUi_IsServedInDevelopmentOnly()
    {
        using var development = DevelopmentClient();

        var inDevelopment = await development.GetAsync("/swagger/index.html");
        var inTesting = await _client.GetAsync("/swagger/index.html");
        var documentInTesting = await _client.GetAsync("/openapi/v2.json");

        Assert.Equal(HttpStatusCode.OK, inDevelopment.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inTesting.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, documentInTesting.StatusCode);
    }

    private static IEnumerable<JsonElement> Operations(JsonDocument document) =>
        document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(method => method.Value);
}
