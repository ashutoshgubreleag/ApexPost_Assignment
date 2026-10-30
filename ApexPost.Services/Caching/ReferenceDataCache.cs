using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;

namespace ApexPost.Services.Caching;

/// <summary>
/// Products and zones are read on almost every request but change rarely. The cache is a singleton,
/// so it must not hold a scoped DbContext: each refill opens its own scope and reads through the
/// query service (AsNoTracking projection to DTOs). Hits complete synchronously (ValueTask).
/// </summary>
public interface IReferenceDataCache
{
    ValueTask<IReadOnlyDictionary<Sku, ProductDto>> GetProductsAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyDictionary<string, ZoneDto>> GetZonesAsync(CancellationToken cancellationToken);

    void InvalidateProducts();
}

public sealed class ReferenceDataCache(IMemoryCache cache, IServiceScopeFactory scopes) : IReferenceDataCache
{
    private const string ProductsKey = "ref:products";
    private const string ZonesKey = "ref:zones";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public ValueTask<IReadOnlyDictionary<Sku, ProductDto>> GetProductsAsync(CancellationToken cancellationToken) =>
        cache.TryGetValue(ProductsKey, out IReadOnlyDictionary<Sku, ProductDto>? hit) && hit is not null
            ? ValueTask.FromResult(hit)
            : new ValueTask<IReadOnlyDictionary<Sku, ProductDto>>(LoadProductsAsync(cancellationToken));

    public ValueTask<IReadOnlyDictionary<string, ZoneDto>> GetZonesAsync(CancellationToken cancellationToken) =>
        cache.TryGetValue(ZonesKey, out IReadOnlyDictionary<string, ZoneDto>? hit) && hit is not null
            ? ValueTask.FromResult(hit)
            : new ValueTask<IReadOnlyDictionary<string, ZoneDto>>(LoadZonesAsync(cancellationToken));

    public void InvalidateProducts() => cache.Remove(ProductsKey);

    private async Task<IReadOnlyDictionary<Sku, ProductDto>> LoadProductsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var all = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>().ListProductsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Sku, ProductDto> index = all.ToDictionary(p => p.Sku);
        return cache.Set(ProductsKey, index, Lifetime);
    }

    private async Task<IReadOnlyDictionary<string, ZoneDto>> LoadZonesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var all = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>().ListZonesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, ZoneDto> index = all.ToDictionary(z => z.ZoneCode, StringComparer.OrdinalIgnoreCase);
        return cache.Set(ZonesKey, index, Lifetime);
    }
}
