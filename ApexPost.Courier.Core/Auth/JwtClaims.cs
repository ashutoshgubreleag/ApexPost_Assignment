using System.Text;
using System.Text.Json;

namespace ApexPost.Courier.Core.Auth;

/// <summary>Reads claims from a JWT payload so the app knows the courier id. The server still validates the token; this is display only.</summary>
public static class JwtClaims
{
    public const string CourierIdClaim = "courierId";

    public static string? GetCourierId(string? token) => GetString(token, CourierIdClaim);

    public static DateTimeOffset? GetExpiry(string? token)
    {
        var json = ReadPayload(token);
        if (json is null)
        {
            return null;
        }

        using (json)
        {
            return json.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
    }

    private static string? GetString(string? token, string claim)
    {
        var json = ReadPayload(token);
        if (json is null)
        {
            return null;
        }

        using (json)
        {
            return json.RootElement.TryGetProperty(claim, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
    }

    private static JsonDocument? ReadPayload(string? token)
    {
        var parts = token?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            return JsonDocument.Parse(Convert.FromBase64String(payload));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Builds an unsigned token for tests (never accepted by the server).</summary>
    public static string BuildForTests(string courierId, DateTimeOffset expires)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"none\"}")}.{B64($"{{\"courierId\":\"{courierId}\",\"exp\":{expires.ToUnixTimeSeconds()}}}")}.x";
    }
}
