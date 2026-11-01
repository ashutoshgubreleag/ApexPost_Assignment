using Microsoft.AspNetCore.Authorization;
using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;

namespace ApexPost.MVC.Security;

/// <summary>Authorization policy names. Policies, not role strings, are referenced by controllers.</summary>
public static class Policies
{
    public const string BranchManagerOnly = nameof(BranchManagerOnly);
    public const string AdministratorOnly = nameof(AdministratorOnly);
    public const string CanTransact = nameof(CanTransact);
    public const string StaffManagers = nameof(StaffManagers);
    public const string AuditViewers = nameof(AuditViewers);
    public const string SameBranchOnly = nameof(SameBranchOnly);

    public static void Configure(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Secure by default: every endpoint requires a signed-in user unless it opts out.
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        options.AddPolicy(BranchManagerOnly, p => p.RequireClaim(ApexPostClaims.Role, nameof(StaffRole.BranchManager)));
        options.AddPolicy(AdministratorOnly, p => p.RequireClaim(ApexPostClaims.Role, nameof(StaffRole.Administrator)));
        options.AddPolicy(CanTransact, p => p.RequireClaim(ApexPostClaims.Role, nameof(StaffRole.CounterClerk), nameof(StaffRole.BranchManager)));
        options.AddPolicy(StaffManagers, p => p.RequireClaim(ApexPostClaims.Role, nameof(StaffRole.Administrator), nameof(StaffRole.BranchManager)));
        options.AddPolicy(AuditViewers, p => p.RequireClaim(ApexPostClaims.Role, nameof(StaffRole.Auditor), nameof(StaffRole.Administrator), nameof(StaffRole.BranchManager)));
        options.AddPolicy(SameBranchOnly, p => p.RequireAuthenticatedUser().AddRequirements(new SameBranchRequirement()));
    }
}
