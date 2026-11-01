using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApexPost.MVC.Models;
using ApexPost.MVC.Security;
using ApexPost.Services.Staff;
using ApexPost.Utilities.Filters;

namespace ApexPost.MVC.Controllers;

public sealed class StaffController(IStaffService staff) : ApexPostController
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await staff.GetAllAsync(Actor, cancellationToken));

    [Authorize(Policy = Policies.StaffManagers)]
    [HttpGet]
    public IActionResult Create() => View(new StaffFormViewModel { HomeBranchCode = User.BranchCode.Value });

    [Authorize(Policy = Policies.StaffManagers)]
    [HttpPost]
    [Audited("Staff.Create", "Email", "Role", "HomeBranchCode")]
    public async Task<IActionResult> Create(StaffFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var created = await staff.CreateAsync(ToCommand(model), Actor, cancellationToken);
        Flash($"{created.Name} added.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = Policies.StaffManagers)]
    [HttpGet]
    public async Task<IActionResult> Edit([FromRoute] int id, CancellationToken cancellationToken)
    {
        var member = await staff.GetByIdAsync(id, cancellationToken);
        return View(new StaffFormViewModel
        {
            Id = member.Id,
            Name = member.Name,
            Email = member.Email,
            Role = member.Role,
            HomeBranchCode = member.HomeBranchCode.Value,
            IsActive = member.IsActive,
        });
    }

    [Authorize(Policy = Policies.StaffManagers)]
    [HttpPost]
    [Audited("Staff.Update", "Id", "Role", "HomeBranchCode", "IsActive")]
    public async Task<IActionResult> Edit([FromRoute] int id, StaffFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Id = id;
        var updated = await staff.UpdateAsync(ToCommand(model), Actor, cancellationToken);
        Flash($"{updated.Name} updated.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = Policies.StaffManagers)]
    [HttpPost]
    [Audited("Staff.Deactivate")]
    public async Task<IActionResult> Deactivate([FromRoute] int id, CancellationToken cancellationToken)
    {
        await staff.DeactivateAsync(id, Actor, cancellationToken);
        Flash("Staff member deactivated. They can no longer sign in or process transactions.");
        return RedirectToAction(nameof(Index));
    }

    private static SaveStaffCommand ToCommand(StaffFormViewModel model) => new()
    {
        Id = model.Id,
        Name = model.Name,
        Email = model.Email,
        Role = model.Role,
        HomeBranchCode = model.HomeBranchCode,
        IsActive = model.IsActive,
        InitialPassword = model.Password,
    };
}
