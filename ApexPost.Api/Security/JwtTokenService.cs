using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ApexPost.Api.Security;

/// <summary>A signed access token and when it stops being valid.</summary>
/// <param name="AccessToken">The compact JWT.</param>
/// <param name="ExpiresAtUtc">Expiry instant (UTC).</param>
internal sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAtUtc);

/// <summary>Issues development access tokens. A real deployment replaces this with an identity provider.</summary>
internal interface ITokenService
{
    /// <summary>Creates a signed token for a courier id and a role (<see cref="ApiRoles.Courier"/> or <see cref="ApiRoles.Admin"/>).</summary>
    IssuedToken Issue(string courierId, string role);
}

/// <summary>Issues HS256 tokens with the same library family (<c>Microsoft.IdentityModel.JsonWebTokens</c>) that JwtBearer validates with.</summary>
internal sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    /// <inheritdoc />
    public IssuedToken Issue(string courierId, string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(courierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [ApiRoles.SubjectClaim] = courierId,
                [ApiRoles.CourierIdClaim] = courierId,
                [ApiRoles.RoleClaim] = role,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
        };

        return new IssuedToken(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
