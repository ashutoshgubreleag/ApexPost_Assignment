using System.Globalization;
using System.Security.Claims;
using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Common;

namespace ApexPost.MVC.Security;

/// <summary>Claim types issued at sign-in. The brief requires StaffId, Role and BranchCode.</summary>
public static class ApexPostClaims
{
    public const string StaffId = "StaffId";
    public const string Role = "Role";
    public const string BranchCode = "BranchCode";
}

/// <summary>
/// C# 14 extension members on <see cref="ClaimsPrincipal"/>: controllers write <c>User.BranchCode</c>
/// and <c>User.ToStaffContext()</c> instead of repeating FindFirst/parse code in every action.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    extension(ClaimsPrincipal user)
    {
        public int StaffId => int.Parse(user.FindFirstValue(ApexPostClaims.StaffId) ?? "0", CultureInfo.InvariantCulture);

        public StaffRole StaffRole => Enum.TryParse<StaffRole>(user.FindFirstValue(ApexPostClaims.Role), out var role) ? role : StaffRole.CounterClerk;

        public BranchCode BranchCode => new(user.FindFirstValue(ApexPostClaims.BranchCode) ?? string.Empty);

        public bool CanSeeAllBranches => user.StaffRole is StaffRole.Administrator or StaffRole.Auditor;

        public StaffContext ToStaffContext() =>
            new(user.StaffId, user.StaffRole, user.BranchCode, user.Identity?.Name ?? string.Empty);
    }

    /// <summary>Builds the principal issued at sign-in.</summary>
    public static ClaimsPrincipal CreatePrincipal(StaffDto staff, string authenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(staff);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, staff.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, staff.Name),
            new(ApexPostClaims.StaffId, staff.Id.ToString(CultureInfo.InvariantCulture)),
            new(ApexPostClaims.Role, staff.Role.ToString()),
            new(ApexPostClaims.BranchCode, staff.HomeBranchCode.Value),
        };

        // roleType = "Role" so [Authorize(Roles=...)] and User.IsInRole read our Role claim.
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationScheme, ClaimTypes.Name, ApexPostClaims.Role));
    }
}
