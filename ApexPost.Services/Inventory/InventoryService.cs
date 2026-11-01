using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.Validation;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Caching;
using ApexPost.Services.Common;
using ApexPost.Services.Events;

namespace ApexPost.Services.Inventory;

public sealed class InventoryService(
    IStockRepository stock,
    IStockAdjustmentProcedure adjustStock,
    ICatalogQueries queries,
    IUnitOfWork unitOfWork,
    IReferenceDataCache referenceData,
    BusinessRules rules,
    IApexPostEvents events,
    TimeProvider clock) : IInventoryService
{
    public Task<IReadOnlyList<StockDto>> GetStockForBranchAsync(BranchCode branch, CancellationToken cancellationToken) =>
        queries.GetStockForBranchAsync(branch, cancellationToken);

    public Task<int> GetAvailableQuantityAsync(BranchCode branch, Sku sku, CancellationToken cancellationToken) =>
        stock.GetQuantityAsync(branch, sku, cancellationToken);

    public async Task DeductAsync(BranchCode branch, IReadOnlyCollection<StockQuantity> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        var crossedIntoLow = await ConcurrencyRetry.ExecuteAsync(unitOfWork, rules.ConcurrencyRetryAttempts, async ct =>
        {
            var low = await RemoveStockAsync(stock, branch, items.Select(i => (i.Sku, i.Quantity)), rules, clock.GetUtcNow(), ct).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            return low;
        }, cancellationToken).ConfigureAwait(false);

        await PublishLowStockAsync(crossedIntoLow, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestockAsync(BranchCode branch, IReadOnlyCollection<StockQuantity> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        await ConcurrencyRetry.ExecuteAsync(unitOfWork, rules.ConcurrencyRetryAttempts, async ct =>
        {
            await ReturnStockAsync(stock, branch, items.Select(i => (i.Sku, i.Quantity)), clock.GetUtcNow(), ct).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StockDto> AdjustAsync(StockAdjustmentCommand command, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        ReflectionValidator.EnsureValid(command);

        var branch = new BranchCode(command.BranchCode);
        var sku = new Sku(command.Sku);
        if (!(actor.IsAdministrator || (actor.IsBranchManager && actor.BranchCode == branch)))
        {
            throw new ForbiddenOperationException("Only the branch manager or an administrator can adjust stock.");
        }

        var products = await referenceData.GetProductsAsync(cancellationToken).ConfigureAwait(false);
        if (!products.TryGetValue(sku, out var product))
        {
            throw new CommandValidationException(nameof(command.Sku), $"Unknown SKU {sku}.");
        }

        if (command.QuantityChange == 0)
        {
            throw new CommandValidationException(nameof(command.QuantityChange), "Enter a non-zero quantity change.");
        }

        var before = await stock.GetQuantityAsync(branch, sku, cancellationToken).ConfigureAwait(false);

        // The procedure locks the row, applies the delta, refuses to go negative and writes the
        // StockAdjustments history row in one database transaction.
        var after = await adjustStock.AdjustAsync(branch, sku, command.QuantityChange, command.Reason.Trim(), $"staff:{actor.StaffId}", cancellationToken).ConfigureAwait(false);

        var current = (await queries.GetStockForBranchAsync(branch, cancellationToken).ConfigureAwait(false)).First(s => s.Sku == sku);
        if (!rules.IsLowStock(before, current.ReorderThreshold, rules.DefaultLowStockThreshold)
            && rules.IsLowStock(after, current.ReorderThreshold, rules.DefaultLowStockThreshold))
        {
            events.Publish(new StockLowEventArgs(branch, sku, product.Name, after, current.ReorderThreshold, clock.GetUtcNow()));
        }

        return current;
    }

    /// <summary>
    /// Stages stock removal for a sale on tracked rows (shared by DeductAsync and the payment use
    /// case, so both commit stock and transaction together). Checks every line first, so a failure
    /// changes nothing. Returns the rows that crossed into "low".
    /// </summary>
    internal static async Task<IReadOnlyList<BranchStock>> RemoveStockAsync(
        IStockRepository stock, BranchCode branch, IEnumerable<(Sku Sku, int Quantity)> items, BusinessRules rules, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var wanted = items.GroupBy(i => i.Sku).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        if (wanted.Count == 0)
        {
            return [];
        }

        var rows = (await stock.GetForUpdateAsync(branch, wanted.Keys, cancellationToken).ConfigureAwait(false)).ToDictionary(r => r.Sku);
        foreach (var (sku, quantity) in wanted)
        {
            var onHand = rows.TryGetValue(sku, out var row) ? row.QuantityOnHand : 0;
            if (quantity > onHand)
            {
                throw new BusinessRuleViolationException("STOCK_INSUFFICIENT", $"Insufficient stock for {sku} at {branch}: {onHand} on hand, {quantity} requested.");
            }
        }

        var crossed = new List<BranchStock>();
        foreach (var (sku, quantity) in wanted)
        {
            var row = rows[sku];
            var wasLow = row.IsLow(rules.DefaultLowStockThreshold);
            row.Remove(quantity, at);
            if (!wasLow && row.IsLow(rules.DefaultLowStockThreshold))
            {
                crossed.Add(row);
            }
        }

        return crossed;
    }

    internal static async Task ReturnStockAsync(IStockRepository stock, BranchCode branch, IEnumerable<(Sku Sku, int Quantity)> items, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var wanted = items.GroupBy(i => i.Sku).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        if (wanted.Count == 0)
        {
            return;
        }

        var rows = await stock.GetForUpdateAsync(branch, wanted.Keys, cancellationToken).ConfigureAwait(false);
        foreach (var row in rows)
        {
            row.Add(wanted[row.Sku], at);
        }
    }

    private async Task PublishLowStockAsync(IReadOnlyList<BranchStock> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var products = await referenceData.GetProductsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in rows)
        {
            var name = products.TryGetValue(row.Sku, out var product) ? product.Name : row.Sku.Value;
            events.Publish(new StockLowEventArgs(row.BranchCode, row.Sku, name, row.QuantityOnHand, row.ReorderThreshold, clock.GetUtcNow()));
        }
    }
}
