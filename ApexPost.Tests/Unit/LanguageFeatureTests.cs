using System.Diagnostics;
using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Models.Exceptions;
using ApexPost.Models.Validation;
using ApexPost.Models.ValueObjects;
using ApexPost.Models.Specifications;
using ApexPost.Services.Caching;
using ApexPost.Services.Common;
using ApexPost.Services.Courier;
using ApexPost.Services.Shipping;
using ApexPost.Tests.Support;
using ApexPost.Utilities.Audit;
using ApexPost.Tests.Support.Fakes;
using Xunit.Abstractions;

namespace ApexPost.Tests.Unit;

public sealed class ValueObjectTests
{
    [Fact]
    public void Money_ExtensionMembers_RoundAndTotal()
    {
        Assert.Equal(new Money(0.13m), new Money(0.125m).RoundedToCents);          // half away from zero
        Assert.Equal(new Money(1.20m), new Money(12m).ApplyRate(0.10m));
        Assert.Equal(new Money(6m), new[] { new Money(1m), new Money(2m), new Money(3m) }.Total());
        Assert.Equal(new Money(4.5m), 4.5m.AsMoney);
        Assert.Equal("$12.50", new Money(12.5m).ToString());
    }

    [Fact]
    public void ValueObjects_HaveValueEquality()
    {
        Assert.Equal(new BranchCode("br-001"), new BranchCode("BR-001"));
        Assert.Equal(new Sku(" pkg-001 "), new Sku("PKG-001"));
        Assert.Equal(new Weight(1.5m), Weight.FromKilograms(1.5m));
        Assert.Equal(new Address("A", "1 St", "City", "nsw", "2000"), new Address("A", "1 St", "City", "NSW", "2000"));
    }

    [Theory]
    [InlineData("BR1")]
    [InlineData("XX-001")]
    [InlineData("")]
    public void BranchCode_RejectsInvalidValues(string value) => Assert.Throws<ArgumentException>(() => new BranchCode(value));

    [Fact]
    public void Weight_CannotBeNegative() => Assert.Throws<ArgumentOutOfRangeException>(() => new Weight(-0.1m));

    [Fact]
    public void Dimensions_ComputeVolumetricWeight() => Assert.Equal(new Weight(14.4m), new Dimensions(60, 40, 30).VolumetricWeight);

    [Theory]
    [InlineData("N1234567", "****4567")]
    [InlineData("pa 98-76-54-32", "****5432")]
    public void MaskedDocumentNumber_KeepsOnlyLastFour(string raw, string expected)
    {
        var masked = MaskedDocumentNumber.FromRaw(raw);

        Assert.Equal(expected, masked.Value);
        Assert.DoesNotContain(raw.Replace(" ", string.Empty, StringComparison.Ordinal)[..4], masked.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaskedDocumentNumber_RefusesToRehydrateAnUnmaskedValue() =>
        Assert.Throws<ArgumentException>(() => MaskedDocumentNumber.FromMasked("N1234567"));
}

public sealed class TaxRuleTests
{
    [Theory]
    [InlineData(TransactionLineType.RetailItem, TaxCategory.Reduced, false, TaxCategory.Reduced)]
    [InlineData(TransactionLineType.Shipping, null, false, TaxCategory.Standard)]
    [InlineData(TransactionLineType.Shipping, null, true, TaxCategory.Exempt)]
    [InlineData(TransactionLineType.MoneyOrder, null, false, TaxCategory.Exempt)]
    [InlineData(TransactionLineType.IdentityVerification, null, false, TaxCategory.Standard)]
    public void PatternMatchedTaxCategory(TransactionLineType line, TaxCategory? product, bool international, TaxCategory expected) =>
        Assert.Equal(expected, TaxRules.CategoryFor(line, product, international));

    [Fact]
    public void Rates() => Assert.Equal([0.10m, 0.05m, 0m], new[] { TaxCategory.Standard, TaxCategory.Reduced, TaxCategory.Exempt }.Select(TaxRules.RateFor));
}

public sealed class ReflectionValidatorTests
{
    private sealed record Sample
    {
        [NotBlank, MaxLengthRule(5)] public string Name { get; init; } = string.Empty;
        [InRange(1, 10)] public int Quantity { get; init; }
        [Matches("^BR-[0-9]{3}$", "a branch code")] public string Branch { get; init; } = "BR-001";
        [DefinedEnum] public StaffRole Role { get; init; }
    }

    [Fact]
    public void ReportsEveryBrokenRule()
    {
        var errors = ReflectionValidator.Validate(new Sample { Name = "toolongname", Quantity = 0, Branch = "nope", Role = (StaffRole)42 });

        Assert.Equal(["Name", "Quantity", "Branch", "Role"], errors.Select(e => e.PropertyName).Distinct());
    }

    [Fact]
    public void CachesMetadataPerType()
    {
        ReflectionValidator.Validate(new Sample { Name = "ok", Quantity = 1 });
        var cached = ReflectionValidator.CachedTypeCount;

        for (var i = 0; i < 100; i++)
        {
            ReflectionValidator.Validate(new Sample { Name = "ok", Quantity = 1 });
        }

        Assert.Equal(cached, ReflectionValidator.CachedTypeCount);
        Assert.Throws<CommandValidationException>(() => ReflectionValidator.EnsureValid(new Sample()));
    }
}

public sealed class SpecificationTests
{
    [Fact]
    public void Composes()
    {
        var paid = Specification<CounterTransaction>.Where(t => t.Status == TransactionStatus.Paid);
        var atBr1 = Specification<CounterTransaction>.Where(t => t.BranchCode == new BranchCode("BR-001"));
        var tx = CounterTransaction.Open(new BranchCode("BR-002"), 1, 10, DateTimeOffset.UtcNow);
        tx.AddServiceLine(TransactionLineType.IdentityVerification, "IDV-1", "Identity check", new Money(2.50m), TaxCategory.Standard, new Money(0.25m), Money.Zero);
        tx.Pay(PaymentMethod.Cash, DateTimeOffset.UtcNow);

        Assert.False((paid & atBr1).IsSatisfiedBy(tx));
        Assert.True((paid | atBr1).IsSatisfiedBy(tx));
        Assert.True((!atBr1).IsSatisfiedBy(tx));
    }
}

public sealed class AuditRedactorTests
{
    [Theory]
    [InlineData("Password")]
    [InlineData("DocumentNumber")]
    [InlineData("X-Api-Key")]
    [InlineData("CardNumber")]
    public void SensitiveNames_AreRedacted(string name) => Assert.Equal(AuditRedactor.Redacted, AuditRedactor.Redact(name, "secret-value"));

    [Fact]
    public void LongDigitRuns_AreMasked() =>
        Assert.Equal("Refund to card ************1234", AuditRedactor.Redact("Reason", "Refund to card 4111111111111234"));

    [Fact]
    public void OrdinaryFields_AreKept() => Assert.Equal("PKG-001", AuditRedactor.Redact("Sku", "PKG-001"));
}

public sealed class ConcurrencyAndCachingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CourierRateLookups_AreBoundedBySemaphore()
    {
        await using var harness = await ServiceHarness.CreateAsync(new Dictionary<string, string?>
        {
            ["Courier:MaxConcurrentRateLookups"] = "3",
            ["Courier:RateLookupLatencyMs"] = "30",
        });
        var courier = (SimulatedCourierRateService)harness.Get<ICourierRateService>();
        var zone = new ZoneDto("Z01", "Metro", false, false, new Money(1.20m), new Money(8.95m), new Money(1.10m));

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => courier.GetFuelSurchargeRateAsync(zone, CancellationToken.None)));

        Assert.InRange(courier.PeakConcurrency, 1, 3);
    }

    [Fact]
    public async Task CourierTracking_TimeoutIsAFailure_ButCallerCancellationIsNot()
    {
        await using var harness = await ServiceHarness.CreateAsync(new Dictionary<string, string?> { ["Courier:TrackingTimeoutMs"] = "400" });
        var tracking = harness.Scoped<ICourierTrackingService>();

        // Our timeout: the caller is still waiting, so return a failure result.
        var slow = await tracking.TrackAsync("APX261020999", CancellationToken.None);
        Assert.IsType<TrackingResult.Unavailable>(slow);

        // The caller gave up: propagate cancellation, do not report a failure.
        using var cts = new CancellationTokenSource(20);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tracking.TrackAsync("APX261020999", cts.Token));
    }

    [Fact]
    public async Task ReferenceDataCache_HitsCompleteSynchronously_AndAreFaster()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var cache = harness.Get<IReferenceDataCache>();
        var repository = harness.Scoped<ICatalogQueries>();

        await cache.GetProductsAsync(CancellationToken.None); // warm
        var hit = cache.GetProductsAsync(CancellationToken.None);
        Assert.True(hit.IsCompletedSuccessfully); // no Task allocated, no await needed

        const int iterations = 2000;
        await repository.ListProductsAsync(CancellationToken.None);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var all = await repository.ListProductsAsync(CancellationToken.None);
            _ = all.First(p => p.Sku == new Sku("OFF-011"));
        }

        var repositoryMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        for (var i = 0; i < iterations; i++)
        {
            var index = await cache.GetProductsAsync(CancellationToken.None);
            _ = index[new Sku("OFF-011")];
        }

        var cacheMs = sw.Elapsed.TotalMilliseconds;
        output.WriteLine($"{iterations} product lookups: repository copy+scan {repositoryMs:0.0} ms, ValueTask cache {cacheMs:0.0} ms ({repositoryMs / Math.Max(cacheMs, 0.001):0}x)");
        Assert.True(cacheMs < repositoryMs);
    }

    [Fact]
    public async Task ShippingQuote_UsesCachedZones()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var shipping = harness.Scoped<IShippingService>();

        var quote = await shipping.QuoteAsync(new ShippingQuoteCommand { ZoneCode = "Z09", WeightKg = 1, LengthCm = 20, WidthCm = 15, HeightCm = 10 }, CancellationToken.None);

        Assert.Equal("Europe & UK", quote.Context.Zone.Name);
        Assert.Equal(0.12m, quote.Context.FuelSurchargeRate);
        Assert.Equal(10, (await shipping.GetZonesAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task AuditStore_IsAppendOnly_UnderConcurrentWrites()
    {
        var store = new InMemoryAuditStore();

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => store.AppendAsync(new AuditEntry { Action = $"Test.{i}", TimestampUtc = DateTimeOffset.UtcNow }, CancellationToken.None)));

        Assert.Equal(50, store.Entries.Count);
        Assert.Equal(50, (await store.ReadRecentAsync(100, CancellationToken.None)).Select(e => e.Action).Distinct().Count());
    }
}
