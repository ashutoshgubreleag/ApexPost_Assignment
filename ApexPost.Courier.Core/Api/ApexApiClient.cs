using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace ApexPost.Courier.Core.Api;

/// <summary>
/// Typed HTTP client for the courier endpoints. <see cref="HttpClient"/> comes from <c>IHttpClientFactory</c> (pooled
/// handlers, no socket exhaustion) and already has the auth handler and the resilience pipeline.
/// Network problems surface as exceptions (HttpRequestException, TimeoutRejectedException, BrokenCircuitException);
/// answers the server gave on purpose surface as <see cref="ApiException"/>; 401 as <see cref="UnauthorizedAccessException"/>.
/// </summary>
public sealed class ApexApiClient(HttpClient http, IOptions<ApiOptions> options)
{
    private readonly ApiOptions _options = options.Value;

    public async Task<string> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        var body = JsonContent.Create(new TokenRequestWire(username, password), ApexJsonContext.Default.TokenRequestWire);
        using var response = await http.PostAsync(_options.TokenPath, body, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
        {
            throw new UnauthorizedAccessException("Wrong user name or password.");
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var token = await response.Content.ReadFromJsonAsync(ApexJsonContext.Default.TokenResponseWire, cancellationToken).ConfigureAwait(false);
        return token?.AccessToken ?? throw new ApiException(response.StatusCode, "The server returned no token.");
    }

    public async Task<ScanUploadResultWire> UploadScansAsync(string trackingNumber, IReadOnlyList<ScanItemWire> scans, CancellationToken cancellationToken)
    {
        var path = string.Format(System.Globalization.CultureInfo.InvariantCulture, _options.ScansPathTemplate, Uri.EscapeDataString(trackingNumber));
        var body = JsonContent.Create(new ScanUploadWire(scans), ApexJsonContext.Default.ScanUploadWire);
        using var response = await http.PostAsync(path, body, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("The session expired. Sign in again.");
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync(ApexJsonContext.Default.ScanUploadResultWire, cancellationToken).ConfigureAwait(false)
            ?? throw new ApiException(response.StatusCode, "The server returned an empty answer.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // 5xx / 408 are retried by the pipeline; when they still arrive here, retries are used up, so report "unreachable".
        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
        {
            throw new HttpRequestException($"Server error {(int)response.StatusCode}.", null, response.StatusCode);
        }

        ProblemWire? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync(ApexJsonContext.Default.ProblemWire, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            // Not Problem Details (for example an HTML error page): fall back to the status text.
        }

        throw new ApiException(response.StatusCode, DescribeProblem(response.StatusCode, problem));
    }

    internal static string DescribeProblem(HttpStatusCode status, ProblemWire? problem)
    {
        if (problem is null)
        {
            return $"The server refused the request ({(int)status}).";
        }

        if (problem.Errors is { Count: > 0 })
        {
            return string.Join(" ", problem.Errors.SelectMany(e => e.Value));
        }

        return problem.Detail ?? problem.Title ?? $"The server refused the request ({(int)status}).";
    }
}
