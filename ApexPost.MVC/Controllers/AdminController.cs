using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.MVC.Security;
using ApexPost.Utilities;


namespace ApexPost.MVC.Controllers;

/// <summary>Audit log viewer for auditors and administrators. Lives under /admin, so it is IP-restricted.</summary>
[IpRestricted]
[Authorize(Roles = nameof(StaffRole.Administrator) + "," + nameof(StaffRole.Auditor))]
public sealed class AdminController(IAuditStore audit) : ApexPostController
{
    public async Task<IActionResult> Audit(int take = 200, CancellationToken cancellationToken = default) =>
        View(await audit.ReadRecentAsync(Math.Clamp(take, 1, 1000), cancellationToken));
}
