using System.Security.Cryptography;
using System.Text;
using ApexPost.Utilities.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ApexPost.Api.Security;

/// <summary>Marks an action as protected by the partner <c>X-Api-Key</c> header (and lets the OpenAPI transformer document the key scheme).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class PartnerApiKeyAttribute() : ServiceFilterAttribute(typeof(PartnerApiKeyFilter))
{
}

/// <summary>
/// Validates <c>X-Api-Key</c> against <c>Partner:ApiKeys</c> (an array from user-secrets or environment, never committed).
/// Mirrors the Week 2 <c>ApiKeyValidationMiddleware</c>: 500 when no key is configured (never fail open),
/// 401 for a missing or wrong key, constant-time comparison over every configured key with no early exit.
/// </summary>
internal sealed partial class PartnerApiKeyFilter(IConfiguration configuration, ILogger<PartnerApiKeyFilter> logger) : IAsyncAuthorizationFilter
{
    /// <summary>Request header carrying the key.</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>Configuration section holding the allowed keys.</summary>
    public const string ConfigurationKey = "Partner:ApiKeys";

    /// <inheritdoc />
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var http = context.HttpContext;

        var keys = configuration.GetSection(ConfigurationKey).GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();

        if (keys.Count == 0)
        {
            LogNotConfigured(http.Request.Path.Value ?? "/");
            await ProblemResponses.WriteAsync(http, new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Partner access not configured",
                Detail = "The partner endpoint is not available. Contact ApexPost support.",
            }).ConfigureAwait(false);
            context.Result = new EmptyResult();
            return;
        }

        var supplied = http.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(supplied) || !Matches(supplied, keys))
        {
            LogRejected(http.Request.Path.Value ?? "/", string.IsNullOrEmpty(supplied) ? "missing" : "invalid");
            http.Response.Headers.WWWAuthenticate = "ApiKey header=\"X-Api-Key\"";
            await ProblemResponses.WriteAsync(http, new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "A valid X-Api-Key header is required.",
            }).ConfigureAwait(false);
            context.Result = new EmptyResult();
        }
    }

    private static bool Matches(string supplied, List<string> keys)
    {
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var matched = false;
        foreach (var key in keys)
        {
            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            matched |= CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash); // no early exit
        }

        return matched;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Partner API keys are not configured; rejected request to {Path}")]
    private partial void LogNotConfigured(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected partner request to {Path}: API key {Reason}")]
    private partial void LogRejected(string path, string reason);
}
