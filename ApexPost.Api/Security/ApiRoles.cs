namespace ApexPost.Api.Security;

/// <summary>Role names and claim types used in the access tokens. One place, so a typo cannot silently lock everyone out.</summary>
public static class ApiRoles
{
    /// <summary>A courier on the road: uploads scans and changes shipment status.</summary>
    public const string Courier = "Courier";

    /// <summary>A back-office administrator: may delete shipments.</summary>
    public const string Admin = "Admin";

    /// <summary>Claim type holding the role (also configured as <c>RoleClaimType</c>, so <c>[Authorize(Roles = ...)]</c> reads it).</summary>
    public const string RoleClaim = "role";

    /// <summary>Claim type holding the courier id, for example <c>C-1001</c>.</summary>
    public const string CourierIdClaim = "courierId";

    /// <summary>Claim type holding the subject (the courier id; also the identity name).</summary>
    public const string SubjectClaim = "sub";

    internal static string? Normalize(string? role) =>
        string.Equals(role, Courier, StringComparison.OrdinalIgnoreCase) ? Courier :
        string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase) ? Admin : null;
}
