namespace ApexPost.Api.Security;

/// <summary>
/// The <c>Jwt</c> configuration section. <c>Issuer</c> and <c>Audience</c> live in appsettings.json.
/// <c>SigningKey</c> is a secret: it comes ONLY from user-secrets or an environment variable (<c>Jwt__SigningKey</c>), never from a committed file.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>Name of the configuration section.</summary>
    public const string SectionName = "Jwt";

    /// <summary>HS256 needs a key of at least 256 bits (32 bytes).</summary>
    public const int MinimumSigningKeyLength = 32;

    /// <summary>Expected <c>iss</c> claim.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Expected <c>aud</c> claim.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Symmetric signing key (secret). At least 32 characters.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Lifetime of tokens issued by <c>/dev/token</c>, in minutes.</summary>
    public int AccessTokenMinutes { get; set; } = 60;

    /// <summary>Allowed clock difference between issuer and API when checking <c>exp</c>/<c>nbf</c>.</summary>
    public int ClockSkewSeconds { get; set; } = 30;
}
