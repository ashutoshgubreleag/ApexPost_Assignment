using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Models.ValueObjects;
using ApexPost.MVC.Models;
using ApexPost.MVC.Security;
using ApexPost.Services.Branch;
using ApexPost.Services.Dashboard;
using ApexPost.Utilities.Filters;

namespace ApexPost.MVC.Controllers;

public sealed class BranchController(IBranchService branches, IDashboardService dashboards) : ApexPostController
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await branches.GetAllAsync(cancellationToken));

    /// <summary>Branch dashboard: sales, stock and alerts loaded in parallel (Task.WhenAll in the service).</summary>
    [Authorize(Policy = Policies.SameBranchOnly)]
    public async Task<IActionResult> Dashboard(string branchCode, CancellationToken cancellationToken)
    {
        if (!BranchCode.TryParse(branchCode, out var code))
        {
            return BadRequest("Invalid branch code.");
        }

        var dashboardTask = dashboards.BuildAsync(code.Value, Actor, cancellationToken);
        var branchesTask = branches.GetAllAsync(cancellationToken);
        await Task.WhenAll(dashboardTask, branchesTask);

        return View(new DashboardViewModel(
            await dashboardTask,
            await branchesTask,
            User.CanSeeAllBranches,
            User.StaffRole is StaffRole.CounterClerk or StaffRole.BranchManager));
    }

    /// <summary>JSON variant of the dashboard figures (for a wallboard or quick checks).</summary>
    [Authorize(Policy = Policies.SameBranchOnly)]
    public async Task<IActionResult> Summary(string branchCode, CancellationToken cancellationToken)
    {
        if (!BranchCode.TryParse(branchCode, out var code))
        {
            return BadRequest();
        }

        var d = await dashboards.BuildAsync(code.Value, Actor, cancellationToken);
        return Json(new
        {
            branch = d.Branch.BranchCode.Value,
            salesDate = d.SalesDate,
            transactions = d.Sales.TransactionCount,
            retail = d.Sales.RetailNet.Amount,
            services = d.Sales.ServicesNet.Amount,
            lowStockItems = d.LowStock.Rows.Count,
            openTransactions = d.OpenTransactionCount,
            buildMs = d.BuildMilliseconds,
        });
    }

    [Authorize(Policy = Policies.AdministratorOnly)]
    [HttpGet]
    public IActionResult Create() => View(new BranchFormViewModel());

    [Authorize(Policy = Policies.AdministratorOnly)]
    [HttpPost]
    [Audited("Branch.Create", "BranchCode", "Name")]
    public async Task<IActionResult> Create(BranchFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        await branches.CreateAsync(ToCommand(model), Actor, cancellationToken);
        Flash($"Branch {model.BranchCode} created.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = Policies.AdministratorOnly)]
    [HttpGet]
    public async Task<IActionResult> Edit(string branchCode, CancellationToken cancellationToken)
    {
        if (!BranchCode.TryParse(branchCode, out var code))
        {
            return NotFound();
        }

        var branch = await branches.GetByCodeAsync(code.Value, cancellationToken);
        return View(new BranchFormViewModel
        {
            BranchCode = branch.BranchCode.Value,
            Name = branch.Name,
            Region = branch.Region,
            OpeningHours = branch.OpeningHours,
            CounterCount = branch.Counters.Count(c => c.IsActive),
            IsEdit = true,
        });
    }

    [Authorize(Policy = Policies.AdministratorOnly)]
    [HttpPost]
    [Audited("Branch.Update", "BranchCode", "Name", "OpeningHours")]
    public async Task<IActionResult> Edit(BranchFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        await branches.UpdateAsync(ToCommand(model), Actor, cancellationToken);
        Flash($"Branch {model.BranchCode} updated.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = Policies.StaffManagers)]
    [HttpPost]
    [Audited("Branch.SetStatus", "status")]
    public async Task<IActionResult> SetStatus(string branchCode, BranchStatus status, CancellationToken cancellationToken)
    {
        if (!BranchCode.TryParse(branchCode, out var code) || !Enum.IsDefined(status))
        {
            return BadRequest();
        }

        await branches.SetStatusAsync(code.Value, status, Actor, cancellationToken);
        Flash($"Branch {code} is now {status}.");
        return RedirectToAction(nameof(Index));
    }

    private static SaveBranchCommand ToCommand(BranchFormViewModel model) => new()
    {
        BranchCode = model.BranchCode,
        Name = model.Name,
        Region = model.Region,
        OpeningHours = model.OpeningHours,
        CounterCount = model.CounterCount,
    };
}
