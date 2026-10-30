using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Models.ValueObjects;
using ApexPost.MVC.Models;
using ApexPost.MVC.Security;
using ApexPost.Services.Catalog;
using ApexPost.Services.Inventory;
using ApexPost.Utilities.Filters;

namespace ApexPost.MVC.Controllers;

public sealed class CatalogController(IProductCatalogService catalog, IInventoryService inventory) : ApexPostController
{
    /// <summary>Product catalogue with the stock position at a branch (own branch unless admin/auditor).</summary>
    [Authorize(Policy = Policies.SameBranchOnly)]
    public async Task<IActionResult> Index(string? branch, ProductCategory? category, CancellationToken cancellationToken)
    {
        BranchCode code;
        if (string.IsNullOrEmpty(branch))
        {
            code = User.BranchCode;
        }
        else if (BranchCode.TryParse(branch, out var parsed))
        {
            code = parsed.Value;
        }
        else
        {
            return BadRequest("Invalid branch code.");
        }

        ViewData["Branch"] = code.Value;
        ViewData["Category"] = category;
        return View(await catalog.GetCatalogueForBranchAsync(code, category, cancellationToken));
    }

    [Authorize(Policy = Policies.StaffManagers)]
    [HttpPost]
    [RedirectOnInvalidModel(nameof(Index))]
    [Audited("Stock.Adjust", "BranchCode", "Sku", "QuantityChange", "Reason")]
    public async Task<IActionResult> AdjustStock(StockAdjustmentViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var row = await inventory.AdjustAsync(new StockAdjustmentCommand
        {
            BranchCode = model.BranchCode,
            Sku = model.Sku,
            QuantityChange = model.QuantityChange,
            Reason = model.Reason,
        }, Actor, cancellationToken);

        Flash($"{row.Sku} at {row.BranchCode} is now {row.QuantityOnHand}.");
        return RedirectToAction(nameof(Index), new { branch = model.BranchCode });
    }

    /// <summary>Set-based price revision for a whole category (one UPDATE via ExecuteUpdateAsync).</summary>
    [Authorize(Roles = nameof(StaffRole.Administrator))]
    [HttpPost]
    [RedirectOnInvalidModel(nameof(Index))]
    [Audited("Catalog.RevisePrices", "Category", "PercentChange", "Reason")]
    public async Task<IActionResult> RevisePrices(RevisePricesViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var changed = await catalog.RevisePricesAsync(new RevisePricesCommand
        {
            Category = model.Category,
            PercentChange = model.PercentChange,
            Reason = model.Reason,
        }, Actor, cancellationToken);

        Flash($"{changed} {model.Category} price(s) revised by {model.PercentChange}%.");
        return RedirectToAction(nameof(Index));
    }
}
