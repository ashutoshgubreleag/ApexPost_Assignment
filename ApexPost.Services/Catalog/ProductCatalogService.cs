using System.Globalization;
using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.Validation;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Caching;
using ApexPost.Services.Common;

namespace ApexPost.Services.Catalog;

public sealed class ProductCatalogService(
    IReferenceDataCache referenceData,
    ICatalogQueries queries,
    IProductRepository products,
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    BusinessRules rules,
    TimeProvider clock) : IProductCatalogService
{
    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken cancellationToken)
    {
        var all = await referenceData.GetProductsAsync(cancellationToken).ConfigureAwait(false);
        return all.Values.Where(p => p.IsActive).OrderBy(p => p.Category).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<ProductDto?> FindProductAsync(Sku sku, CancellationToken cancellationToken)
    {
        var all = await referenceData.GetProductsAsync(cancellationToken).ConfigureAwait(false);
        return all.TryGetValue(sku, out var product) && product.IsActive ? product : null;
    }

    public async Task<IReadOnlyList<CatalogueItem>> GetCatalogueForBranchAsync(BranchCode branch, ProductCategory? category, CancellationToken cancellationToken)
    {
        // Sequential on purpose: both reads may use the request's single DbContext.
        var catalogue = await GetProductsAsync(cancellationToken).ConfigureAwait(false);
        var stock = await queries.GetStockForBranchAsync(branch, cancellationToken).ConfigureAwait(false);
        var stockBySku = stock.ToDictionary(s => s.Sku);

        return catalogue
            .Where(p => category is null || p.Category == category)
            .Select(p => stockBySku.TryGetValue(p.Sku, out var row)
                ? new CatalogueItem(p, row.QuantityOnHand, row.ReorderThreshold, rules.IsLowStock(row.QuantityOnHand, row.ReorderThreshold, rules.DefaultLowStockThreshold))
                : new CatalogueItem(p, 0, 0, true))
            .ToList();
    }

    public async Task<int> RevisePricesAsync(RevisePricesCommand command, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.IsAdministrator)
        {
            throw new ForbiddenOperationException("Only administrators can revise prices.");
        }

        ReflectionValidator.EnsureValid(command);
        var factor = 1m + command.PercentChange / 100m;

        // ExecuteUpdate runs immediately; the notification is a normal SaveChanges. Both happen in one
        // explicit transaction (with the execution strategy) so a failure leaves prices untouched.
        var changed = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var rows = await products.RevisePricesAsync(command.Category, factor, ct).ConfigureAwait(false);
            notifications.Add(Notification.Create(
                NotificationType.PriceRevision,
                null,
                "pricing@apexpost.local",
                string.Create(CultureInfo.InvariantCulture, $"{rows} {command.Category} prices changed by {command.PercentChange:+0.##;-0.##}% by staff {actor.StaffId}: {command.Reason.Trim()}"),
                string.Empty,
                clock.GetUtcNow()));
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            return rows;
        }, cancellationToken).ConfigureAwait(false);

        referenceData.InvalidateProducts();
        return changed;
    }
}
