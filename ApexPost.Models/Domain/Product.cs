using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;

namespace ApexPost.Models.Domain;

/// <summary>A retail product in the national catalogue.</summary>
public sealed class Product : Entity
{
    private Product()
    {
        // For EF Core materialisation.
    }

    public Sku Sku { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public ProductCategory Category { get; private set; }

    // Stored as a plain decimal field so the database can revise prices set-based (ExecuteUpdate
    // can only do arithmetic on provider types); the domain still only exposes Money.
    private decimal _unitPrice;

    /// <summary>Tax-exclusive shelf price.</summary>
    public Money UnitPrice => new(_unitPrice);

    public TaxCategory TaxCategory { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static Product Create(Sku sku, string name, ProductCategory category, Money unitPrice, TaxCategory taxCategory)
    {
        var product = new Product { Sku = sku };
        product.Rename(name);
        product.Category = Guard.Defined(category, nameof(category));
        product.TaxCategory = Guard.Defined(taxCategory, nameof(taxCategory));
        product.RevisePrice(unitPrice);
        return product;
    }

    public void Rename(string name) => Name = Guard.Text(name, nameof(name), 120);

    public void RevisePrice(Money unitPrice)
    {
        if (unitPrice <= Money.Zero)
        {
            throw new BusinessRuleViolationException("PRICE_INVALID", "A unit price must be greater than zero.");
        }

        _unitPrice = unitPrice.RoundedToCents.Amount;
    }

    public void Deactivate() => IsActive = false;
}

/// <summary>
/// Quantity of one SKU held at one branch. The invariant "stock never goes negative" lives here
/// (and again as a CHECK constraint); concurrent sales are caught by a rowversion shadow property.
/// </summary>
public sealed class BranchStock : Entity
{
    private BranchStock()
    {
        // For EF Core materialisation.
    }

    public BranchCode BranchCode { get; private set; }

    public Sku Sku { get; private set; }

    public int QuantityOnHand { get; private set; }

    public int ReorderThreshold { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static BranchStock Create(BranchCode branch, Sku sku, int quantity, int reorderThreshold, DateTimeOffset at)
    {
        if (quantity < 0)
        {
            throw new BusinessRuleViolationException("STOCK_NEGATIVE", $"Stock for {sku} at {branch} cannot go below zero.");
        }

        return new BranchStock
        {
            BranchCode = branch,
            Sku = sku,
            QuantityOnHand = quantity,
            ReorderThreshold = Math.Max(0, reorderThreshold),
            UpdatedAt = at,
        };
    }

    /// <summary>Rule: a sale can take stock to zero but never below it.</summary>
    public void Remove(int quantity, DateTimeOffset at)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (quantity > QuantityOnHand)
        {
            throw new BusinessRuleViolationException(
                "STOCK_INSUFFICIENT",
                $"Insufficient stock for {Sku} at {BranchCode}: {QuantityOnHand} on hand, {quantity} requested.");
        }

        QuantityOnHand -= quantity;
        UpdatedAt = at;
    }

    public void Add(int quantity, DateTimeOffset at)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        QuantityOnHand += quantity;
        UpdatedAt = at;
    }

    /// <summary>
    /// Offline-sync policy (CONFLICTS.md, C2): the goods already left the counter, so the sale is
    /// accepted but stock stops at zero. Returns the shortfall (units sold that were not on hand).
    /// </summary>
    public int RemoveUpToAvailable(int quantity, DateTimeOffset at)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        var taken = Math.Min(quantity, QuantityOnHand);
        QuantityOnHand -= taken;
        UpdatedAt = at;
        return quantity - taken;
    }

    public bool IsLow(int defaultThreshold) => QuantityOnHand <= Math.Max(ReorderThreshold, defaultThreshold);
}
