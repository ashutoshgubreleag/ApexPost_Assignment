using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Security;

/// <summary>Body of <c>POST /dev/token</c>.</summary>
/// <param name="CourierId">Courier id, for example <c>C-1001</c>.</param>
/// <param name="Role"><c>Courier</c> or <c>Admin</c>.</param>
internal sealed record DevTokenRequest(string? CourierId, string? Role);

/// <summary>Response of <c>POST /dev/token</c>. The MAUI app codes against exactly these two property names.</summary>
/// <param name="AccessToken">The compact JWT to send as <c>Authorization: Bearer ...</c>.</param>
/// <param name="ExpiresAtUtc">When the token expires (UTC).</param>
internal sealed record DevTokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Development-only token endpoint (stands in for the identity provider's token endpoint). It is mapped only
/// when the environment is Development, so in every other environment the route simply does not exist (404).
/// </summary>
internal static class DevTokenEndpoint
{
    /// <summary>Maps <c>POST /dev/token</c>. Call only inside an <c>IsDevelopment()</c> check.</summary>
    public static IEndpointRouteBuilder MapDevToken(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/dev/token", Handle)
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithName("IssueDevToken");
        return endpoints;
    }

    private static IResult Handle(DevTokenRequest? request, ITokenService tokens)
    {
        var courierId = request?.CourierId?.Trim();
        var role = ApiRoles.Normalize(request?.Role);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(courierId) || courierId.Length > 50)
        {
            errors["courierId"] = ["courierId is required (1 to 50 characters)."];
        }

        if (role is null)
        {
            errors["role"] = [$"role must be '{ApiRoles.Courier}' or '{ApiRoles.Admin}'."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors, title: "Invalid token request", statusCode: StatusCodes.Status400BadRequest);
        }

        var issued = tokens.Issue(courierId!, role!);
        return Results.Ok(new DevTokenResponse(issued.AccessToken, issued.ExpiresAtUtc));
    }
}
