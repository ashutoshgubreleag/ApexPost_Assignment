using System.Net;
using System.Text;

namespace ApexPost.UnitTests.Client;

/// <summary>
/// Stands in for the network: every request is answered by a script, nothing leaves the process.
/// Records the last request (method, path, body) so tests can assert what the client sent.
/// </summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }

    public HttpMethod? LastMethod { get; private set; }

    public string? LastPath { get; private set; }

    public string? LastBody { get; private set; }

    public static FakeHttpMessageHandler Returning(HttpStatusCode status, string? body = null, string mediaType = "application/json") =>
        new((_, _) => Task.FromResult(Response(status, body, mediaType)));

    /// <summary>A server that never answers: the call only ends when the token is cancelled (timeout or caller).</summary>
    public static FakeHttpMessageHandler Hanging() =>
        new(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
        });

    public static HttpResponseMessage Response(HttpStatusCode status, string? body, string mediaType = "application/json") =>
        new(status) { Content = body is null ? null : new StringContent(body, Encoding.UTF8, mediaType) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        LastMethod = request.Method;
        LastPath = request.RequestUri?.AbsolutePath;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return await respond(request, cancellationToken);
    }
}
