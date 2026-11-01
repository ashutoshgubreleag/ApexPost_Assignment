using System.Globalization;
using ApexPost.Contracts;
using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;
using ApexPost.MVC.Composition;
using ApexPost.Services.Sync;
using ApexPost.Utilities;
using ApexPost.Utilities.Middleware;
using ApexPost.Utilities.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ApexPost.MVC.Controllers;

/// <summary>
/// Offline-sync API for counter terminals. HTTPS only (HSTS + redirection), authenticated by
/// device id + key (DeviceAuthenticationMiddleware), rate limited per device, errors as Problem
/// Details. The controller only maps the wire contract to an application command; every rule
/// lives in <see cref="IOfflineSaleService"/>, which uses the same domain model and unit of work
/// as a sale at a live counter.
/// </summary>
[ApiController]
[Route("api/sync")]
[JsonApi]
[AllowAnonymous]
[DeviceAuthenticated]
[IgnoreAntiforgeryToken]
[EnableRateLimiting(WebServiceRegistration.DeviceRateLimitPolicy)]
public sealed class SyncController(IOfflineSaleService offlineSales, IOptions<SyncOptions> options) : ControllerBase
{
    private string DeviceId => (string)HttpContext.Items[DeviceAuthenticationMiddleware.DeviceItemKey]!;

    private BranchCode DeviceBranch => new((string)HttpContext.Items[DeviceAuthenticationMiddleware.BranchItemKey]!);

    /// <summary>Push a batch of offline sales. Safe to retry: duplicates return the original result.</summary>
    [HttpPost("transactions")]
    public async Task<ActionResult<PushTransactionsResponse>> PushTransactions(PushTransactionsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SchemaVersion != SyncContract.SchemaVersion)
        {
            throw new CommandValidationException(nameof(request.SchemaVersion), $"Unsupported schema version {request.SchemaVersion}; expected {SyncContract.SchemaVersion}.");
        }

        if (request.Sales is not { Count: > 0 } || request.Sales.Count > options.Value.MaxSalesPerPush)
        {
            throw new CommandValidationException(nameof(request.Sales), $"Send between 1 and {options.Value.MaxSalesPerPush} sales per request.");
        }

        var results = new List<OfflineSaleResultDto>(request.Sales.Count);
        foreach (var sale in request.Sales)
        {
            try
            {
                var result = await offlineSales.RecordAsync(ToCommand(sale), cancellationToken);
                results.Add(new OfflineSaleResultDto(result.LocalTransactionId, result.Outcome.ToString(), result.ServerTransactionId, result.Message, result.Warnings, result.WasDuplicate));
            }
            catch (CommandValidationException ex)
            {
                // A malformed sale is reported back and stays pending on the device for a fix; it
                // does not fail the rest of the batch.
                results.Add(new OfflineSaleResultDto(sale.LocalTransactionId, "Invalid", null, ex.Message, [], false));
            }
        }

        return Ok(new PushTransactionsResponse(results));
    }

    /// <summary>Catalogue, prices and this branch's stock, changed since the given time (all when omitted).</summary>
    [HttpGet("branch-snapshot")]
    public async Task<ActionResult<BranchSnapshotResponse>> BranchSnapshot([FromQuery] DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var snapshot = await offlineSales.GetSnapshotAsync(DeviceBranch, since, cancellationToken);
        return Ok(new BranchSnapshotResponse(
            SyncContract.SchemaVersion,
            snapshot.BranchCode.Value,
            snapshot.GeneratedAt,
            since is not null,
            snapshot.Products.Select(p => new ProductSnapshotDto(p.Sku.Value, p.Name, p.Category.ToString(), p.UnitPrice.Amount, p.TaxCategory.ToString(), p.IsActive)).ToList(),
            snapshot.Stock.Select(s => new StockSnapshotDto(s.Sku.Value, s.QuantityOnHand, s.ReorderThreshold)).ToList()));
    }

    /// <summary>
    /// Explicit input-model → command mapping. The device id and branch come from the
    /// authenticated device, never from the body, so a terminal cannot post into another branch.
    /// </summary>
    private RecordOfflineSaleCommand ToCommand(OfflineSaleDto sale) => new()
    {
        DeviceId = DeviceId,
        LocalTransactionId = sale.LocalTransactionId,
        BranchCode = DeviceBranch.Value,
        CounterNumber = sale.CounterNumber,
        ClerkStaffId = sale.ClerkStaffId,
        CapturedAt = sale.CapturedAtUtc,
        PaymentMethod = Enum.TryParse<PaymentMethod>(sale.PaymentMethod, ignoreCase: true, out var method) && Enum.IsDefined(method)
            ? method
            : throw new CommandValidationException(nameof(sale.PaymentMethod), string.Create(CultureInfo.InvariantCulture, $"Unknown payment method '{sale.PaymentMethod}'.")),
        Lines = (sale.Lines ?? []).Select(l => new OfflineSaleLine { Sku = l.Sku, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList(),
        MoneyOrders = (sale.MoneyOrders ?? []).Select(m => new OfflineMoneyOrder { Amount = m.Amount, Sender = m.Sender, Recipient = m.Recipient }).ToList(),
    };
}
