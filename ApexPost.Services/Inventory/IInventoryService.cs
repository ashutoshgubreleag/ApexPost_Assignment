using ApexPost.Models.Validation;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Common;

namespace ApexPost.Services.Inventory;

public sealed record StockQuantity(Sku Sku, int Quantity);

public sealed record StockAdjustmentCommand
{
    [NotBlank, Matches("^BR-[0-9]{3}$", "in the form BR-000")]
    public string BranchCode { get; init; } = string.Empty;

    [NotBlank, Matches("^[A-Za-z]{3}-[0-9]{3}$", "a SKU such as PKG-001")]
    public string Sku { get; init; } = string.Empty;

    /// <summary>Positive to receive stock, negative to write off.</summary>
    [InRange(-1000, 1000)]
    public int QuantityChange { get; init; }

    [NotBlank, MaxLengthRule(200)]
    public string Reason { get; init; } = string.Empty;
}

public interface IInventoryService
{
    Task<IReadOnlyList<StockDto>> GetStockForBranchAsync(BranchCode branch, CancellationToken cancellationToken);

    Task<int> GetAvailableQuantityAsync(BranchCode branch, Sku sku, CancellationToken cancellationToken);

    /// <summary>
    /// Removes stock for a sale, all or nothing, in one commit. Fails with STOCK_INSUFFICIENT if any
    /// line would take a branch below zero; nothing is changed in that case. Concurrent sales of
    /// the same rows are detected with rowversion and retried on fresh data.
    /// </summary>
    Task DeductAsync(BranchCode branch, IReadOnlyCollection<StockQuantity> items, CancellationToken cancellationToken);

    /// <summary>Returns stock to the shelf (one commit).</summary>
    Task RestockAsync(BranchCode branch, IReadOnlyCollection<StockQuantity> items, CancellationToken cancellationToken);

    /// <summary>Manual adjustment by a branch manager or administrator, through usp_AdjustStock.</summary>
    Task<StockDto> AdjustAsync(StockAdjustmentCommand command, StaffContext actor, CancellationToken cancellationToken);
}
