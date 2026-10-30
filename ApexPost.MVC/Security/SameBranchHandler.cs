using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;

namespace ApexPost.MVC.Security;

/// <summary>"SameBranchOnly": the branch in the URL must be the user's own branch.</summary>
public sealed class SameBranchRequirement : IAuthorizationRequirement;

/// <summary>
/// Compares the <c>branchCode</c> route value (or query string) with the BranchCode claim.
/// Administrators and auditors pass (they oversee every branch). With endpoint routing the
/// authorization resource is the <see cref="HttpContext"/>, so route values are available here.
/// The service layer repeats the check on the data itself; this stops the request earlier.
/// </summary>
public sealed class SameBranchHandler : AuthorizationHandler<SameBranchRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SameBranchRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = context.User;
        var role = user.FindFirst(ApexPostClaims.Role)?.Value;
        if (role is nameof(StaffRole.Administrator) or nameof(StaffRole.Auditor))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var requested = context.Resource is HttpContext http
            ? http.GetRouteValue("branchCode")?.ToString() ?? http.Request.Query["branch"].ToString()
            : null;
        var own = user.FindFirst(ApexPostClaims.BranchCode)?.Value;

        if (string.IsNullOrEmpty(requested) || string.Equals(requested, own, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
