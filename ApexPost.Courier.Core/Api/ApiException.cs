using System.Net;

namespace ApexPost.Courier.Core.Api;

/// <summary>The server answered with a non-success status that retrying cannot fix (400, 403, 404, 409...). Carries the Problem Details text.</summary>
public sealed class ApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
