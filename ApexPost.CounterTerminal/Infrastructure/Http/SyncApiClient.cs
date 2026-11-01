using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ApexPost.Contracts;
using ApexPost.CounterTerminal.Application;

namespace ApexPost.CounterTerminal.Infrastructure.Http;

/// <summary>
/// HTTP client for the server's /api/sync endpoints. Sends the device headers on every call
/// (the key comes from user-secrets). Anything that means "cannot reach the server right now"
/// (network error, timeout, 5xx, 429, simulated offline switch) becomes
/// <see cref="SyncUnavailableException"/> so the worker keeps the sales Pending and backs off.
/// A 400/401/403 is a configuration problem and is reported as such.
/// </summary>
public sealed class SyncApiClient : ISyncApiClient
{
    private readonly HttpClient _http;
    private readonly INetworkSwitch _network;

    public SyncApiClient(HttpClient http, TerminalOptions options, INetworkSwitch network)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        _http = http;
        _network = network;
        _http.BaseAddress ??= new Uri(options.ServerBaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.Remove(SyncHeaders.DeviceId);
        _http.DefaultRequestHeaders.Remove(SyncHeaders.DeviceKey);
        _http.DefaultRequestHeaders.Add(SyncHeaders.DeviceId, options.DeviceId);
        _http.DefaultRequestHeaders.Add(SyncHeaders.DeviceKey, options.DeviceKey);
    }

    public Task<PushTransactionsResponse> PushAsync(PushTransactionsRequest request, CancellationToken cancellationToken) =>
        SendAsync<PushTransactionsResponse>(ct => _http.PostAsJsonAsync("api/sync/transactions", request, ct), cancellationToken);

    public Task<BranchSnapshotResponse> PullAsync(DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var path = since is { } s
            ? "api/sync/branch-snapshot?since=" + Uri.EscapeDataString(s.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
            : "api/sync/branch-snapshot";
        return SendAsync<BranchSnapshotResponse>(ct => _http.GetAsync(path, ct), cancellationToken);
    }

    private async Task<T> SendAsync<T>(Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        if (_network.IsDisconnected)
        {
            throw new SyncUnavailableException("Offline (network switched off at this terminal).");
        }

        HttpResponseMessage response;
        try
        {
            response = await send(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new SyncUnavailableException("Server unreachable: " + ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SyncUnavailableException("Server did not answer in time.", ex);
        }

        using (response)
        {
            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new SyncUnavailableException($"Server busy or failing ({(int)response.StatusCode}); will retry.");
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new SyncUnavailableException($"Device not authorised ({(int)response.StatusCode}). Check Terminal:DeviceId and the DeviceKey user-secret.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                // Kept Pending (never dropped) and surfaced on the status line for a supervisor.
                throw new SyncUnavailableException($"Sync request refused ({(int)response.StatusCode}): {Truncate(detail)}");
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false)
                ?? throw new SyncUnavailableException("Empty response from server.");
        }
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "...";
}
