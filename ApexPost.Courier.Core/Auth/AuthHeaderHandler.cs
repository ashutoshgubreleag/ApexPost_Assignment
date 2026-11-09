using System.Net;
using System.Net.Http.Headers;

namespace ApexPost.Courier.Core.Auth;

/// <summary>
/// Adds <c>Authorization: Bearer {jwt}</c> to every API call. It sits outside the resilience pipeline, so a retry
/// reuses the same header. A 401 clears the stored token (the session is over), so the app shows the sign-in page next time.
/// </summary>
public sealed class AuthHeaderHandler(ITokenStore tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAsync().ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(token))
        {
            await tokens.ClearAsync().ConfigureAwait(false);
        }

        return response;
    }
}
