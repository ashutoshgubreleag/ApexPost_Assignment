#if HAS_GENERATED_CLIENT
using ApexPost.Client;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

// Consumer of the generated SDK. Needs the API running (dotnet run --project .\ApexPost.Api --launch-profile https).
// The base address comes from APEXPOST_API_URL or the first argument; certificate validation is never switched off:
// trust the dev certificate once with `dotnet dev-certs https --trust`.
var baseAddress = new Uri(args.FirstOrDefault() ?? Environment.GetEnvironmentVariable("APEXPOST_API_URL") ?? "https://localhost:7153/");

// Since Part D every shipment route needs a bearer token. Development only: ask the dev token endpoint for an Admin token
// (it stands in for the identity provider; it does not exist outside Development).
string accessToken;
using (var tokenHttp = new HttpClient { BaseAddress = baseAddress })
{
    try
    {
        var tokenResponse = await tokenHttp.PostAsJsonAsync("dev/token", new { courierId = "C-1001", role = "Admin" });
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<DevToken>();
        accessToken = token?.AccessToken ?? throw new InvalidOperationException("The token endpoint returned no token.");
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"Could not get a token from {baseAddress}: {ex.Message}. Is ApexPost.Api running in Development?");
        return 2;
    }
}

var services = new ServiceCollection();
services.AddApexPostClient(baseAddress).AddHttpMessageHandler(() => new BearerHandler(accessToken));
await using var provider = services.BuildServiceProvider();
var api = provider.GetRequiredService<IApexPostClient>();

var trackingNumber = $"APX-{Random.Shared.Next(10_000_000, 99_999_999)}";
Console.WriteLine($"API: {baseAddress}  tracking number: {trackingNumber}\n");

try
{
    // 1. Create (201)
    var created = await api.CreateShipmentAsync(new CreateShipmentRequest
    {
        TrackingNumber = trackingNumber,
        RecipientName = "Ada Lovelace",
        RecipientPhone = "+61 400 000 111",
        Address = "12 Example Street",
        City = "Melbourne",
        WeightKg = 2.5,
    });
    Console.WriteLine($"Created   {created.TrackingNumber} for {created.Recipient.Name}, status {created.Status}");

    // 2. Upload two scans, twice: the second call is a safe retry (idempotent)
    var upload = new ScanUploadRequest
    {
        Scans =
        [
            new ScanItemRequest { ScanId = Guid.NewGuid(), ScanType = ScanType.PickedUp, Location = "Melbourne Depot", ScannedAtUtc = DateTimeOffset.UtcNow.AddHours(-3) },
            new ScanItemRequest { ScanId = Guid.NewGuid(), ScanType = ScanType.Departed, Location = "Melbourne Depot", ScannedAtUtc = DateTimeOffset.UtcNow.AddHours(-2) },
        ],
    };
    var first = await api.UploadScansAsync(trackingNumber, upload);
    var second = await api.UploadScansAsync(trackingNumber, upload);
    Console.WriteLine($"Upload 1  accepted {first.Accepted}, duplicates {first.DuplicatesSkipped}");
    Console.WriteLine($"Upload 2  accepted {second.Accepted}, duplicates {second.DuplicatesSkipped}  (same batch resent)");

    // 3. Read it back: v2 embeds the latest scan
    var shipment = await api.GetShipmentAsync(trackingNumber);
    Console.WriteLine($"Get       status {shipment.Status}, latest scan {shipment.LatestScan?.ScanType} at {shipment.LatestScan?.Location}");

    // 4. List (typed, paged)
    var page = await api.ListShipmentsAsync(page: 1, pageSize: 5, status: null, city: "Melbourne", cancellationToken: CancellationToken.None);
    Console.WriteLine($"List      {page.Items.Count} of {page.TotalCount} shipments in Melbourne");

    // 5. A business-rule failure arrives as a typed exception carrying the ProblemDetails
    try
    {
        await api.DeleteShipmentAsync(trackingNumber);
    }
    catch (ApiException ex)
    {
        Console.WriteLine($"Delete    refused with HTTP {ex.StatusCode}: {ex.Response}");
    }
}
catch (ApiException ex)
{
    Console.WriteLine($"API error HTTP {ex.StatusCode}: {ex.Response}");
    return 1;
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"Could not reach {baseAddress}: {ex.Message}. Is ApexPost.Api running?");
    return 2;
}

return 0;

internal sealed record DevToken(string AccessToken, DateTimeOffset ExpiresAtUtc);

internal sealed class BearerHandler(string accessToken) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return base.SendAsync(request, cancellationToken);
    }
}
#else
Console.WriteLine("The generated client is missing. Run scripts\\export-contracts.ps1, then scripts\\generate-client.ps1, then build again.");
return 3;
#endif
