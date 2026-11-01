using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApexPost.Database.Seeding;

/// <summary>
/// Fictional reference data (6 branches, 24 staff, 32 products, 192 stock rows, 10 zones),
/// embedded in this assembly. The InitialCreate migration seeds it with HasData, and the unit-test
/// fakes load the same rows, so both back-ends start from identical data.
/// </summary>
public static class ReferenceData
{
    /// <summary>Fixed timestamp for seeded audit columns, so migrations are deterministic.</summary>
    public static readonly DateTime SeededAtUtc = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Lazy<IReadOnlyList<BranchSeed>> LazyBranches = new(() => Load<BranchSeed>("Branch"));
    private static readonly Lazy<IReadOnlyList<StaffSeed>> LazyStaff = new(() => Load<StaffSeed>("Staff"));
    private static readonly Lazy<IReadOnlyList<ProductSeed>> LazyProducts = new(() => Load<ProductSeed>("Product"));
    private static readonly Lazy<IReadOnlyList<StockSeed>> LazyStock = new(() => Load<StockSeed>("BranchStock"));
    private static readonly Lazy<IReadOnlyList<ZoneSeed>> LazyZones = new(() => Load<ZoneSeed>("ShippingZone"));

    public static IReadOnlyList<BranchSeed> Branches => LazyBranches.Value;

    public static IReadOnlyList<StaffSeed> Staff => LazyStaff.Value;

    public static IReadOnlyList<ProductSeed> Products => LazyProducts.Value;

    public static IReadOnlyList<StockSeed> Stock => LazyStock.Value;

    public static IReadOnlyList<ZoneSeed> Zones => LazyZones.Value;

    private static IReadOnlyList<T> Load<T>(string name)
    {
        var resource = $"ApexPost.Seed.{name}.json";
        using var stream = typeof(ReferenceData).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded seed file {resource} is missing.");
        return JsonSerializer.Deserialize<List<T>>(stream, Options) ?? [];
    }
}

public sealed record CounterSeed(int Id, string Name, bool IsActive);

public sealed record BranchSeed(int Id, string BranchCode, string Name, string Region, string OpeningHours, string Status, IReadOnlyList<CounterSeed> Counters);

public sealed record StaffSeed(int Id, string Name, string Email, string Role, string HomeBranchCode, bool IsActive, string PasswordHash);

public sealed record ProductSeed(int Id, string Sku, string Name, string Category, decimal UnitPrice, string TaxCategory, bool IsActive);

public sealed record StockSeed(int Id, string BranchCode, string Sku, int QuantityOnHand, int ReorderThreshold, DateTimeOffset UpdatedAt);

public sealed record ZoneSeed(int Id, string ZoneCode, string Name, bool IsInternational, bool IsRemote, decimal LetterBaseRate, decimal ParcelBaseRate, decimal PerKgRate);
