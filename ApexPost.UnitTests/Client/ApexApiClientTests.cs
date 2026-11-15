using System.Net;
using ApexPost.Courier.Core.Api;
using Microsoft.Extensions.Options;

namespace ApexPost.UnitTests.Client;

/// <summary>
/// Risk R5 (TESTING-STRATEGY.md): how the Week 4 Part D typed client maps HTTP answers. The HttpClient is built directly
/// on a fake handler, WITHOUT the Polly pipeline, so these tests isolate our mapping:
///   success  -> parsed result
///   4xx      -> ApiException carrying the ProblemDetails text (the server said no; retrying cannot help)
///   5xx/408  -> HttpRequestException (treated as "unreachable": scans stay Pending)
///   401      -> UnauthorizedAccessException (sign in again)
/// Retry and circuit-breaker behaviour is covered separately by ApexPost.Courier.Core.Tests (real pipeline).
/// No real network is used: requests never leave the process.
/// </summary>
public sealed class ApexApiClientTests
{
    private const string Tn = "APX-10000001";
    private static readonly Uri BaseAddress = new("https://apexpost.test/");
    private static readonly DateTimeOffset ScannedAt = new(2026, 1, 15, 7, 30, 0, TimeSpan.Zero);

    private static ApexApiClient CreateClient(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var http = new HttpClient(handler) { BaseAddress = BaseAddress };
        if (timeout is { } t)
        {
            http.Timeout = t;
        }

        return new ApexApiClient(http, Options.Create(new ApiOptions { BaseAddress = BaseAddress }));
    }

    private static ScanItemWire[] OneScan(Guid? scanId = null) =>
        [new ScanItemWire(scanId ?? Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), ApiScanType.PickedUp, "Melbourne Depot", ScannedAt, null)];

    [Fact]
    public async Task UploadScansAsync_200WithJson_ReturnsParsedResultAndSendsExpectedRequest()
    {
        // Arrange
        var rejectedId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK,
            $$"""{"accepted":1,"duplicatesSkipped":2,"rejected":[{"scanId":"{{rejectedId}}","reason":"Location is required."}]}""");
        var client = CreateClient(handler);

        // Act
        var result = await client.UploadScansAsync(Tn, OneScan(), CancellationToken.None);

        // Assert: the answer is parsed ...
        Assert.Equal(1, result.Accepted);
        Assert.Equal(2, result.DuplicatesSkipped);
        var rejected = Assert.Single(result.Rejected);
        Assert.Equal(rejectedId, rejected.ScanId);
        Assert.Equal("Location is required.", rejected.Reason);
        // ... and the request matches the published v2 contract
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal($"/api/v2/shipments/{Tn}/scans", handler.LastPath);
        Assert.Contains("\"scanType\":\"PickedUp\"", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"scanId\":\"aaaaaaaa-0000-0000-0000-000000000001\"", handler.LastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("recipientSignatureName", handler.LastBody, StringComparison.Ordinal); // nulls are omitted
    }

    [Fact]
    public async Task UploadScansAsync_404ProblemDetails_ThrowsApiExceptionWithTheServersDetail()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.NotFound,
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"detail":"Shipment APX-10000001 was not found."}""",
            "application/problem+json");
        var client = CreateClient(handler);

        // Act
        var ex = await Assert.ThrowsAsync<ApiException>(() => client.UploadScansAsync(Tn, OneScan(), CancellationToken.None));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("Shipment APX-10000001 was not found.", ex.Message);
    }

    [Fact]
    public async Task UploadScansAsync_ValidationProblemDetails_ThrowsApiExceptionListingEveryFieldError()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.BadRequest,
            """
            {"title":"One or more validation errors occurred.","status":400,
             "errors":{"Scans[0].Location":["Location is required."],"Scans[0].ScannedAtUtc":["A scan cannot be in the future."]}}
            """,
            "application/problem+json");
        var client = CreateClient(handler);

        // Act
        var ex = await Assert.ThrowsAsync<ApiException>(() => client.UploadScansAsync(Tn, OneScan(), CancellationToken.None));

        // Assert: field errors win over the generic title, so the courier sees what to fix
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("Location is required.", ex.Message, StringComparison.Ordinal);
        Assert.Contains("A scan cannot be in the future.", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("One or more validation errors", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadScansAsync_ProblemDetailsWithTitleOnly_UsesTheTitle()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.Conflict,
            """{"title":"The shipment is already Delivered.","status":409}""", "application/problem+json");
        var client = CreateClient(handler);

        // Act
        var ex = await Assert.ThrowsAsync<ApiException>(() => client.UploadScansAsync(Tn, OneScan(), CancellationToken.None));

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
        Assert.Equal("The shipment is already Delivered.", ex.Message);
    }

    [Fact]
    public async Task UploadScansAsync_ErrorBodyIsNotJson_FallsBackToTheStatusCode()
    {
        // Arrange: for example an HTML error page from a proxy
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.Forbidden, "<html><body>Forbidden</body></html>", "text/html");
        var client = CreateClient(handler);

        // Act
        var ex = await Assert.ThrowsAsync<ApiException>(() => client.UploadScansAsync(Tn, OneScan(), CancellationToken.None));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Equal("The server refused the request (403).", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task UploadScansAsync_ServerErrorOrRequestTimeout_ThrowsHttpRequestExceptionNotApiException(HttpStatusCode status)
    {
        // Arrange: a 5xx/408 means "try again later", never "the server rejected these scans"
        var handler = FakeHttpMessageHandler.Returning(status, """{"title":"Server error"}""", "application/problem+json");
        var client = CreateClient(handler);

        // Act
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.UploadScansAsync(Tn, OneScan(), CancellationToken.None));

        // Assert
        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public async Task UploadScansAsync_401_ThrowsUnauthorizedAccess()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.Unauthorized);
        var client = CreateClient(handler);

        // Act + Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.UploadScansAsync(Tn, OneScan(), CancellationToken.None));
    }

    [Fact]
    public async Task UploadScansAsync_200WithEmptyBody_ThrowsApiException()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, "null");
        var client = CreateClient(handler);

        // Act
        var ex = await Assert.ThrowsAsync<ApiException>(() => client.UploadScansAsync(Tn, OneScan(), CancellationToken.None));

        // Assert
        Assert.Equal("The server returned an empty answer.", ex.Message);
    }

    [Fact]
    public async Task UploadScansAsync_ServerNeverAnswers_TimesOutWithTaskCanceledFromTimeout()
    {
        // Arrange: a short HttpClient timeout against a server that never answers
        var client = CreateClient(FakeHttpMessageHandler.Hanging(), timeout: TimeSpan.FromMilliseconds(100));

        // Act
        var ex = await Assert.ThrowsAsync<TaskCanceledException>(() => client.UploadScansAsync(Tn, OneScan(), CancellationToken.None));

        // Assert: .NET marks a client timeout with an inner TimeoutException, which is how the uploader tells it
        // apart from the caller cancelling.
        Assert.IsType<TimeoutException>(ex.InnerException);
    }

    [Fact]
    public async Task UploadScansAsync_CallerCancels_ThrowsOperationCanceled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var handler = new FakeHttpMessageHandler(async (_, token) =>
        {
            await cts.CancelAsync(); // the user leaves the page while the request is in flight
            token.ThrowIfCancellationRequested();
            return FakeHttpMessageHandler.Response(HttpStatusCode.OK, "{}");
        });
        var client = CreateClient(handler);

        // Act
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.UploadScansAsync(Tn, OneScan(), cts.Token));

        // Assert: caller cancellation, not a timeout
        Assert.IsNotType<TimeoutException>(ex.InnerException);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task UploadScansAsync_TrackingNumberWithReservedCharacters_IsEscapedInThePath()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":[]}""");
        var client = CreateClient(handler);

        // Act
        await client.UploadScansAsync("APX/../admin", OneScan(), CancellationToken.None);

        // Assert: the value cannot escape its path segment
        Assert.Equal("/api/v2/shipments/APX%2F..%2Fadmin/scans", handler.LastPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetDevelopmentTokenAsync_BlankCourierId_ThrowsArgumentAndSendsNothing(string courierId)
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, """{"accessToken":"x"}""");
        var client = CreateClient(handler);

        // Act
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.GetDevelopmentTokenAsync(courierId, CancellationToken.None));

        // Assert
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task GetDevelopmentTokenAsync_200WithoutToken_ThrowsApiException()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, """{"accessToken":"","expiresAtUtc":null}""");
        var client = CreateClient(handler);

        // Act
        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetDevelopmentTokenAsync("C-1001", CancellationToken.None));

        // Assert
        Assert.Equal("The server returned no token.", ex.Message);
    }
}
