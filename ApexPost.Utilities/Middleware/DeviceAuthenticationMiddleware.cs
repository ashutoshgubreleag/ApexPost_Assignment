using System.Security.Cryptography;
using System.Text;
using ApexPost.Contracts;
using ApexPost.Utilities.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApexPost.Utilities.Middleware;

/// <summary>
/// Authenticates counter terminals on endpoints marked <see cref="DeviceAuthenticatedAttribute"/>.
/// The device sends X-Device-Id and X-Device-Key over HTTPS; the key is compared in constant time
/// (SHA-256 then FixedTimeEquals) and the request is bound to the branch the device is registered
/// to, so a terminal can only push sales for, and pull the snapshot of, its own branch.
/// </summary>
public sealed partial class DeviceAuthenticationMiddleware(
    RequestDelegate next,
    IOptionsMonitor<SyncOptions> options,
    ILogger<DeviceAuthenticationMiddleware> logger)
{
    public const string DeviceItemKey = "ApexPost.DeviceId";
    public const string BranchItemKey = "ApexPost.DeviceBranch";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.GetEndpoint()?.Metadata.GetMetadata<DeviceAuthenticatedAttribute>() is null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var deviceId = context.Request.Headers[SyncHeaders.DeviceId].ToString();
        var key = context.Request.Headers[SyncHeaders.DeviceKey].ToString();

        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrEmpty(key)
            || !options.CurrentValue.Devices.TryGetValue(deviceId, out var device)
            || string.IsNullOrEmpty(device.Key)
            || !KeysMatch(key, device.Key))
        {
            LogRejected(context.Request.Path.Value ?? "/", deviceId.Length > 50 ? deviceId[..50] : deviceId);
            context.Response.Headers.WWWAuthenticate = "DeviceKey headers=\"X-Device-Id, X-Device-Key\"";
            await ProblemResponses.WriteAsync(context, new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "A registered X-Device-Id and its X-Device-Key are required.",
            }).ConfigureAwait(false);
            return;
        }

        context.Items[DeviceItemKey] = deviceId;
        context.Items[BranchItemKey] = device.BranchCode;
        await next(context).ConfigureAwait(false);
    }

    private static bool KeysMatch(string supplied, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected sync request to {Path} from device '{DeviceId}'")]
    private partial void LogRejected(string path, string deviceId);
}
