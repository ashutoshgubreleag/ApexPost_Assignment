using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApexPost.MVC.Models;
using ApexPost.MVC.Security;
using ApexPost.Services.Branch;
using ApexPost.Services.Transactions;
using ApexPost.Utilities.Filters;

namespace ApexPost.MVC.Controllers;

/// <summary>
/// Counter transaction screens. Every POST ends in a redirect (Post/Redirect/Get); business-rule
/// failures are turned into a message + redirect by the DomainExceptionFilter, so the actions
/// contain no try/catch and no rules.
/// </summary>
public sealed class TransactionController(ITransactionService transactions, IBranchService branches) : ApexPostController
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await transactions.GetRecentAsync(Actor, null, 100, cancellationToken));

    [Authorize(Policy = Policies.CanTransact)]
    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var branch = await branches.GetByCodeAsync(User.BranchCode, cancellationToken);
        return View(new OpenTransactionViewModel
        {
            BranchCode = branch.BranchCode.Value,
            BranchName = branch.Name,
            Counters = branch.Counters.Where(c => c.IsActive).ToList(),
        });
    }

    [Authorize(Policy = Policies.CanTransact)]
    [HttpPost]
    [RedirectOnInvalidModel(nameof(Create))]
    [Audited("Transaction.Open", "CounterTerminalId")]
    public async Task<IActionResult> Create(OpenTransactionViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var opened = await transactions.OpenAsync(Actor, model.CounterTerminalId, cancellationToken);
        return RedirectToAction(nameof(Details), new { id = opened.Id });
    }

    /// <summary>The basket screen: lines, totals, add-item/service forms, payment, void/refund.</summary>
    public async Task<IActionResult> Details([FromRoute] int id, CancellationToken cancellationToken) =>
        View(await transactions.GetDetailsAsync(id, Actor, cancellationToken));

    [Authorize(Policy = Policies.CanTransact)]
    [HttpPost]
    [RedirectOnInvalidModel(nameof(Details))]
    [Audited("Transaction.AddItem", "Sku", "Quantity")]
    public async Task<IActionResult> AddItem([FromRoute] int id, AddItemViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        await transactions.AddRetailItemAsync(id, new AddRetailItemCommand { Sku = model.Sku, Quantity = model.Quantity }, Actor, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = Policies.CanTransact)]
    [HttpPost]
    [Audited("Transaction.RemoveLine", "lineNumber")]
    public async Task<IActionResult> RemoveLine([FromRoute] int id, int lineNumber, CancellationToken cancellationToken)
    {
        await transactions.RemoveLineAsync(id, lineNumber, Actor, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = Policies.CanTransact)]
    [HttpPost]
    [RedirectOnInvalidModel(nameof(Details))]
    [Audited("Transaction.Pay", "PaymentMethod")]
    public async Task<IActionResult> Pay([FromRoute] int id, PaymentViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var paid = await transactions.PayAsync(id, model.PaymentMethod, Actor, cancellationToken);
        Flash($"Payment of {paid.TotalAmount} taken by {paid.PaymentMethod}. Transaction {paid.Id} complete.");
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Branch Manager only (policy) and a reason (model + service).</summary>
    [Authorize(Policy = Policies.BranchManagerOnly)]
    [HttpPost]
    [RedirectOnInvalidModel(nameof(Details))]
    [Audited("Transaction.Void", "Reason")]
    public async Task<IActionResult> Void([FromRoute] int id, ReasonViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        await transactions.VoidAsync(id, new VoidOrRefundCommand { Reason = model.Reason }, Actor, cancellationToken);
        Flash($"Transaction {id} voided.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = Policies.BranchManagerOnly)]
    [HttpPost]
    [RedirectOnInvalidModel(nameof(Details))]
    [Audited("Transaction.Refund", "Reason")]
    public async Task<IActionResult> Refund([FromRoute] int id, ReasonViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        await transactions.RefundAsync(id, new VoidOrRefundCommand { Reason = model.Reason }, Actor, cancellationToken);
        Flash($"Transaction {id} refunded and stock returned.");
        return RedirectToAction(nameof(Details), new { id });
    }
}
