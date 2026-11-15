using System.Net;
using System.Text;
using System.Text.Json;
using ApexPost.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.IntegrationTests.Api;

/// <summary>
/// End to end through the real HTTP pipeline and a real SQL Server database (TESTING-STRATEGY.md §8):
/// request -> routing -> auth -> model validation -> controller -> service -> EF Core -> SQL Server -> response.
/// Risks R2, R3, R4. Each test uses its own tracking numbers, so the shared database never couples the tests.
/// Results are checked in the HTTP response AND directly in the database.
/// </summary>
public sealed class ShipmentApiEndToEndTests(SqlApiFactory factory) : IClassFixture<SqlApiFactory>
{
    private const string ProblemJson = "application/problem+json";
    private const string CourierId = "C-2001";

    // Fixed, clearly-past scan times: the API compares scan times with its own clock ("not in the future").
    private static readonly DateTimeOffset ScanTime = new(2026, 1, 15, 7, 0, 0, TimeSpan.Zero);

    private static int _next;

    private static string NextTrackingNumber() => $"APX-{30000000 + Interlocked.Increment(ref _next):D8}";

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static StringContent CreateBody(string tn) => Json($$"""
        {"trackingNumber":"{{tn}}","recipientName":"Test Recipient","recipientPhone":"+61 400 000 111","address":"1 Test Street","city":"Melbourne","weightKg":2.5}
        """);

    private static StringContent ScansBody(params object[] scans) => Json(JsonSerializer.Serialize(new { scans }));

    private static object Scan(Guid id, string type, int minute, string? courierId = null, string? signature = null) =>
        new { scanId = id, scanType = type, location = "Melbourne Depot", scannedAtUtc = ScanTime.AddMinutes(minute), recipientSignatureName = signature, courierId };

    private async Task<int> ScanRowsAsync(string tn)
    {
        await using var db = factory.Database.NewContext();
        return await db.ShipmentScans.CountAsync(s => db.TrackedShipments.Any(t => t.Id == s.ShipmentId && t.TrackingNumber == tn));
    }

    [Fact]
    public async Task CreateUploadAndRead_CourierJourney_IsPersistedAndReflectedInV2()
    {
        // Arrange
        var tn = NextTrackingNumber();
        using var courier = factory.ClientAs(ApiRoles.Courier, CourierId);

        // Act
        var created = await courier.PostAsync("/api/v2/shipments", CreateBody(tn));
        var uploaded = await courier.PostAsync($"/api/v2/shipments/{tn}/scans",
            ScansBody(Scan(Guid.NewGuid(), "PickedUp", 0, CourierId), Scan(Guid.NewGuid(), "Arrived", 30, CourierId)));
        var read = await courier.GetAsync($"/api/v2/shipments/{tn}");

        // Assert: HTTP
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.EndsWith($"/api/v2/shipments/{tn}", created.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        using var upload = JsonDocument.Parse(await uploaded.Content.ReadAsStringAsync());
        Assert.Equal(2, upload.RootElement.GetProperty("accepted").GetInt32());
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var shipment = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal("InTransit", shipment.RootElement.GetProperty("status").GetString());
        Assert.Equal("Arrived", shipment.RootElement.GetProperty("latestScan").GetProperty("scanType").GetString());

        // Assert: database
        await using var db = factory.Database.NewContext();
        var row = await db.TrackedShipments.SingleAsync(s => s.TrackingNumber == tn);
        Assert.Equal(ApexPost.Models.Domain.TrackingStatus.InTransit, row.Status);
        Assert.Equal(2, await ScanRowsAsync(tn));
    }

    [Fact]
    public async Task UploadScans_SameBatchTwice_ReportsDuplicatesAndStoresNoExtraRows()
    {
        // Arrange
        var tn = NextTrackingNumber();
        using var courier = factory.ClientAs(ApiRoles.Courier, CourierId);
        await courier.PostAsync("/api/v2/shipments", CreateBody(tn));
        var batch = new[] { Scan(Guid.NewGuid(), "PickedUp", 0), Scan(Guid.NewGuid(), "Arrived", 10) };
        await courier.PostAsync($"/api/v2/shipments/{tn}/scans", ScansBody(batch));

        // Act: the courier app retries after losing the first response
        var retry = await courier.PostAsync($"/api/v2/shipments/{tn}/scans", ScansBody(batch));

        // Assert
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var body = JsonDocument.Parse(await retry.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("accepted").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("duplicatesSkipped").GetInt32());
        Assert.Equal(2, await ScanRowsAsync(tn));
    }

    [Fact]
    public async Task UploadScans_ScansRecordedByAnotherCourier_Returns403AndStoresNothing()
    {
        // Arrange
        var tn = NextTrackingNumber();
        using var courier = factory.ClientAs(ApiRoles.Courier, CourierId);
        await courier.PostAsync("/api/v2/shipments", CreateBody(tn));

        // Act
        var response = await courier.PostAsync($"/api/v2/shipments/{tn}/scans", ScansBody(Scan(Guid.NewGuid(), "PickedUp", 0, courierId: "C-9999")));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0, await ScanRowsAsync(tn));
    }

    [Fact]
    public async Task Delete_ByCourier_Returns403AndTheShipmentStays()
    {
        // Arrange
        var tn = NextTrackingNumber();
        using var courier = factory.ClientAs(ApiRoles.Courier, CourierId);
        await courier.PostAsync("/api/v2/shipments", CreateBody(tn));

        // Act
        var response = await courier.DeleteAsync($"/api/v2/shipments/{tn}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var db = factory.Database.NewContext();
        Assert.True(await db.TrackedShipments.AnyAsync(s => s.TrackingNumber == tn));
    }

    [Fact]
    public async Task Delete_ByAdmin_Returns204AndRemovesTheRow()
    {
        // Arrange
        var tn = NextTrackingNumber();
        using var courier = factory.ClientAs(ApiRoles.Courier, CourierId);
        await courier.PostAsync("/api/v2/shipments", CreateBody(tn));
        using var admin = factory.ClientAs(ApiRoles.Admin);

        // Act
        var response = await admin.DeleteAsync($"/api/v2/shipments/{tn}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = factory.Database.NewContext();
        Assert.False(await db.TrackedShipments.AnyAsync(s => s.TrackingNumber == tn));
    }

    [Fact]
    public async Task AnyRoute_Anonymous_Returns401()
    {
        // Arrange
        using var anonymous = factory.ClientAs(role: null);

        // Act
        var response = await anonymous.GetAsync("/api/v2/shipments");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateTrackingNumber_Returns409ProblemDetailsAndKeepsOneRow()
    {
        // Arrange
        var tn = NextTrackingNumber();
        using var courier = factory.ClientAs(ApiRoles.Courier, CourierId);
        await courier.PostAsync("/api/v2/shipments", CreateBody(tn));

        // Act
        var response = await courier.PostAsync("/api/v2/shipments", CreateBody(tn));

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DUPLICATE_TRACKING_NUMBER", body.RootElement.GetProperty("errorCode").GetString());
        await using var db = factory.Database.NewContext();
        Assert.Equal(1, await db.TrackedShipments.CountAsync(s => s.TrackingNumber == tn));
    }
}
