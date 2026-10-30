using ApexPost.Models.Domain;
using ApexPost.Models.Validation;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Common;

namespace ApexPost.Services.Catalog;

/// <summary>Request DTO for a set-based price revision of one category.</summary>
public sealed record RevisePricesCommand
{
    [DefinedEnum]
    public ProductCategory Category { get; init; }

    /// <summary>Percentage change, e.g. 5 for +5% or -10 for a 10% cut.</summary>
    [InRange(-50, 50)]
    public decimal PercentChange { get; init; }

    [NotBlank, MaxLengthRule(200)]
    public string Reason { get; init; } = string.Empty;
}

public interface IProductCatalogService
{
    Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken cancellationToken);

    Task<ProductDto?> FindProductAsync(Sku sku, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogueItem>> GetCatalogueForBranchAsync(BranchCode branch, ProductCategory? category, CancellationToken cancellationToken);

    /// <summary>Administrator only. One UPDATE for the whole category (ExecuteUpdateAsync); returns rows changed.</summary>
    Task<int> RevisePricesAsync(RevisePricesCommand command, StaffContext actor, CancellationToken cancellationToken);
}
