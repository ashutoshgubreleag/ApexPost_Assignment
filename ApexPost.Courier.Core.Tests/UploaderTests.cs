using System.Net;
using System.Text.Json;
using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Core.Resilience;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Core.Tests;

public sealed class UploaderTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Accepted_scan_is_succeeded_and_request_is_shaped_correctly()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.Created, """{"accepted":1,"duplicatesSkipped":0,"rejected":[]}"""));
        using var host = new Host(handler);
        var scan = host.AddScan(ScanType.Pickup);

        var outcomes = await host.Get<IShipmentUploader>().UploadAsync([scan], Ct);

        Assert.Single(outcomes);
        Assert.True(outcomes[0].Succeeded);
        var seen = Assert.Single(handler.Requests);
        Assert.Equal("/api/v2/shipments/APX-10000001/scans", seen.Path);
        Assert.Equal("Bearer t0ken", seen.Authorization);
        using var json = JsonDocument.Parse(seen.Body);
        var item = json.RootElement.GetProperty("scans")[0];
        Assert.Equal(scan.ScanId, item.GetProperty("scanId").GetGuid());
        Assert.Equal("PickedUp", item.GetProperty("scanType").GetString());
        Assert.Equal("Melbourne Depot", item.GetProperty("location").GetString());
        Assert.False(item.TryGetProperty("recipientSignatureName", out _));
    }

    [Fact]
    public async Task Duplicate_is_a_success_so_resending_is_safe()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK, """{"accepted":0,"duplicatesSkipped":1,"rejected":[]}"""));
        using var host = new Host(handler);

        var outcomes = await host.Get<IShipmentUploader>().UploadAsync([host.AddScan()], Ct);

        Assert.True(outcomes[0].Succeeded);
    }

    [Fact]
    public async Task Rejected_scan_fails_with_the_servers_reason_and_others_succeed()
    {
        var rejectedId = Guid.Empty;
        var handler = new StubHandler((_, _) => StubHandler.Json(
            HttpStatusCode.Created,
            $$"""{"accepted":1,"duplicatesSkipped":0,"rejected":[{"scanId":"{{rejectedId}}","reason":"Shipment already delivered."}]}"""));
        using var host = new Host(handler);
        var a = host.AddScan(ScanType.Pickup);
        var b = host.AddScan(ScanType.InTransit);
        rejectedId = a.ScanId; // the stub reads this when it answers (after the scans exist)

        var outcomes = await host.Get<IShipmentUploader>().UploadAsync([a, b], Ct);

        Assert.False(outcomes.Single(o => o.ScanId == a.ScanId).Succeeded);
        Assert.Equal("Shipment already delivered.", outcomes.Single(o => o.ScanId == a.ScanId).Error);
        Assert.True(outcomes.Single(o => o.ScanId == b.ScanId).Succeeded);
    }

    [Fact]
    public async Task Delivered_scan_sends_the_signature_name_from_notes()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.Created, """{"accepted":1,"duplicatesSkipped":0,"rejected":[]}"""));
        using var host = new Host(handler);

        await host.Get<IShipmentUploader>().UploadAsync([host.AddScan(ScanType.Delivered, notes: "A. Lovelace")], Ct);

        using var json = JsonDocument.Parse(handler.Requests.Single().Body);
        var item = json.RootElement.GetProperty("scans")[0];
        Assert.Equal("Delivered", item.GetProperty("scanType").GetString());
        Assert.Equal("A. Lovelace", item.GetProperty("recipientSignatureName").GetString());
    }

    [Fact]
    public async Task Delivered_scan_without_a_signature_fails_locally_without_a_request()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("must not be called"));
        using var host = new Host(handler);

        var outcomes = await host.Get<IShipmentUploader>().UploadAsync([host.AddScan(ScanType.Delivered)], Ct);

        Assert.False(outcomes[0].Succeeded);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Scans_for_two_shipments_become_two_requests()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.Created, """{"accepted":1,"duplicatesSkipped":0,"rejected":[]}"""));
        using var host = new Host(handler);

        await host.Get<IShipmentUploader>().UploadAsync([host.AddScan(tracking: "APX-10000001"), host.AddScan(tracking: "APX-10000002")], Ct);

        Assert.Equal(2, handler.Calls);
        Assert.Equal(new[] { "/api/v2/shipments/APX-10000001/scans", "/api/v2/shipments/APX-10000002/scans" }, handler.Requests.Select(r => r.Path).Order().ToArray());
    }

    [Fact]
    public async Task Validation_problem_marks_scans_failed_and_is_not_retried()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"Scans[0].Location":["Location is required."]}}""",
            "application/problem+json"));
        using var host = new Host(handler);

        var outcomes = await host.Get<IShipmentUploader>().UploadAsync([host.AddScan()], Ct);

        Assert.False(outcomes[0].Succeeded);
        Assert.Contains("Location is required.", outcomes[0].Error);
        Assert.Equal(1, handler.Calls); // 400 is the server's answer: no retry
    }

    [Fact]
    public async Task Unknown_shipment_404_is_failed_not_retried()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"Shipment APX-10000001 was not found."}""", "application/problem+json"));
        using var host = new Host(handler);

        var outcomes = await host.Get<IShipmentUploader>().UploadAsync([host.AddScan()], Ct);

        Assert.False(outcomes[0].Succeeded);
        Assert.Equal("Shipment APX-10000001 was not found.", outcomes[0].Error);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Unauthorized_clears_the_token_and_keeps_scans_pending()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var host = new Host(handler);

        await Assert.ThrowsAsync<UploaderUnavailableException>(() => host.Get<IShipmentUploader>().UploadAsync([host.AddScan()], Ct));

        Assert.Null(await host.Tokens.GetAsync());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Server_error_is_retried_then_reported_unavailable()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var host = new Host(handler); // MaxRetryAttempts = 2 -> 3 calls

        await Assert.ThrowsAsync<UploaderUnavailableException>(() => host.Get<IShipmentUploader>().UploadAsync([host.AddScan()], Ct));

        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Transient_failure_then_success_is_retried_to_success()
    {
        var handler = new StubHandler((n, _) => n < 3
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : StubHandler.Json(HttpStatusCode.Created, """{"accepted":1,"duplicatesSkipped":0,"rejected":[]}"""));
        using var host = new Host(handler);

        var outcomes = await host.Get<IShipmentUploader>().UploadAsync([host.AddScan()], Ct);

        Assert.True(outcomes[0].Succeeded);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Network_error_is_retried_then_reported_unavailable()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("No route to host"));
        using var host = new Host(handler);

        await Assert.ThrowsAsync<UploaderUnavailableException>(() => host.Get<IShipmentUploader>().UploadAsync([host.AddScan()], Ct));

        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Circuit_opens_after_repeated_failures_and_then_stops_calling_the_server()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var settings = Host.Fast();
        settings.CircuitMinimumThroughput = 2;
        settings.CircuitFailureRatio = 0.5;
        settings.CircuitBreakDuration = TimeSpan.FromMinutes(5);
        using var host = new Host(handler, settings);
        var uploader = host.Get<IShipmentUploader>();
        var scan = host.AddScan();

        await Assert.ThrowsAsync<UploaderUnavailableException>(() => uploader.UploadAsync([scan], Ct));
        var callsWhenOpened = handler.Calls;
        await Assert.ThrowsAsync<UploaderUnavailableException>(() => uploader.UploadAsync([scan], Ct));

        Assert.Equal(CircuitStateKind.Open, host.Get<ICircuitStateMonitor>().State);
        Assert.Equal(callsWhenOpened, handler.Calls); // open circuit: the second upload never reached the network
    }

    [Fact]
    public async Task Cancelling_the_caller_is_not_reported_as_unavailable()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("never reached"));
        using var host = new Host(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Get<IShipmentUploader>().UploadAsync([host.AddScan()], cts.Token));
    }

    [Fact]
    public void Every_local_scan_type_maps_to_a_server_scan_type()
    {
        Assert.Equal(ApiScanType.PickedUp, ScanTypeMapper.ToApi(ScanType.Pickup));
        Assert.Equal(ApiScanType.Departed, ScanTypeMapper.ToApi(ScanType.InTransit));
        Assert.Equal(ApiScanType.Delivered, ScanTypeMapper.ToApi(ScanType.Delivered));
        Assert.Equal(ApiScanType.DeliveryFailed, ScanTypeMapper.ToApi(ScanType.Failed));
        Assert.All(Enum.GetValues<ScanType>(), t => ScanTypeMapper.ToApi(t));
    }
}
