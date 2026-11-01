using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApexPost.MVC.Models;
using ApexPost.MVC.Security;
using ApexPost.Services.Shipping;
using ApexPost.Services.Transactions;
using ApexPost.Utilities.Filters;

namespace ApexPost.MVC.Controllers;

public sealed class ShippingController(IShippingService shipping, ITransactionService transactions) : ApexPostController
{
    private const string QuoteTempDataKey = "LastQuote";

    /// <summary>Stand-alone price calculator. The POST stores the result and redirects here (PRG).</summary>
    [HttpGet]
    public IActionResult Index()
    {
        var model = TempData.Peek(QuoteTempDataKey + ".Form") is string form
            ? JsonSerializer.Deserialize<QuoteFormViewModel>(form) ?? new QuoteFormViewModel()
            : new QuoteFormViewModel();
        ViewData["Quote"] = TempData[QuoteTempDataKey] as string;
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Index(QuoteFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var quote = await shipping.QuoteAsync(ToCommand(model), cancellationToken);
        TempData[QuoteTempDataKey] = JsonSerializer.Serialize(QuoteSummary.From(quote));
        TempData[QuoteTempDataKey + ".Form"] = JsonSerializer.Serialize(model);
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = Policies.CanTransact)]
    [HttpGet]
    public IActionResult Add([FromRoute] int id) => View(new ShipmentFormViewModel { TransactionId = id });

    [Authorize(Policy = Policies.CanTransact)]
    [HttpPost]
    [Audited("Transaction.AddShipment", "ShippingType", "ServiceLevel", "ZoneCode", "WeightKg")]
    public async Task<IActionResult> Add([FromRoute] int id, ShipmentFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var command = new AddShipmentCommand(
            ToCommand(new QuoteFormViewModel
            {
                ShippingType = model.ShippingType,
                ServiceLevel = model.ServiceLevel,
                ZoneCode = model.ZoneCode,
                WeightKg = model.WeightKg,
                LengthCm = model.LengthCm,
                WidthCm = model.WidthCm,
                HeightCm = model.HeightCm,
            }),
            new RecipientCommand
            {
                RecipientName = model.RecipientName,
                Line1 = model.Line1,
                City = model.City,
                State = model.State,
                Postcode = model.Postcode,
            });

        var shipment = await transactions.AddShipmentAsync(id, command, Actor, cancellationToken);
        Flash($"Shipment {shipment.TrackingNumber} added for {shipment.Price} + tax.");
        return RedirectToAction("Details", "Transaction", new { id });
    }

    /// <summary>Shipping label preview for a shipment.</summary>
    public async Task<IActionResult> Label([FromRoute] int id, CancellationToken cancellationToken) =>
        View(await shipping.GetShipmentAsync(id, Actor, cancellationToken));

    private static ShippingQuoteCommand ToCommand(QuoteFormViewModel model) => new()
    {
        ShippingType = model.ShippingType,
        ServiceLevel = model.ServiceLevel,
        ZoneCode = model.ZoneCode,
        WeightKg = model.WeightKg,
        LengthCm = model.LengthCm,
        WidthCm = model.WidthCm,
        HeightCm = model.HeightCm,
    };
}

/// <summary>Serializable view of a quote for TempData (PRG).</summary>
public sealed record QuoteSummary(
    string Zone,
    string Service,
    string ChargeableWeight,
    string BasePostage,
    string ServiceLoading,
    IReadOnlyList<string> Surcharges,
    string Net,
    string TaxCategory,
    string Tax,
    string Total)
{
    public static QuoteSummary From(ShippingQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        return new QuoteSummary(
            $"{quote.Context.Zone.ZoneCode} {quote.Context.Zone.Name}",
            $"{quote.Context.ShippingType} / {quote.Context.ServiceLevel}",
            quote.ChargeableWeight.ToString(),
            quote.BasePostage.ToString(),
            quote.ServiceLoading.ToString(),
            quote.Surcharges.Select(s => $"{s.Name}: {s.Amount}").ToList(),
            quote.Net.ToString(),
            quote.TaxCategory.ToString(),
            quote.Tax.ToString(),
            quote.Total.ToString());
    }
}
