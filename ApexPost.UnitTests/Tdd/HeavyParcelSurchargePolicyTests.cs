using ApexPost.Services.Tracking;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ApexPost.UnitTests.Tdd;

/// <summary>
/// Week 5 TDD rule (TDD-LOG.md): "Shipments over 30 kg require a heavy-parcel surcharge."
///   up to and including 30 kg       -> 0.00
///   over 30 kg, up to and incl. 50  -> 15.00
///   over 50 kg, up to and incl. 70  -> 25.00
///   below 0.1 kg or over 70 kg      -> ArgumentOutOfRangeException (outside the service range)
/// Only shipments booked on or after the configured effective date pay it.
/// Written BEFORE the implementation existed (RED commit).
/// </summary>
public sealed class HeavyParcelSurchargePolicyTests
{
    private static readonly DateTimeOffset EffectiveFrom = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AfterEffective = EffectiveFrom.AddDays(10);

    private static HeavyParcelSurchargePolicy CreatePolicy(DateTimeOffset now) =>
        CreatePolicy(new FakeTimeProvider(now));

    private static HeavyParcelSurchargePolicy CreatePolicy(TimeProvider clock) =>
        new(Options.Create(new HeavyParcelSurchargeOptions { EffectiveFromUtc = EffectiveFrom }), clock);

    private static decimal Kg(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("0.1")]     // lightest allowed parcel
    [InlineData("1")]
    [InlineData("29.999")]
    [InlineData("30")]      // exactly the threshold: "over 30 kg" means 30 itself pays nothing
    public void SurchargeFor_WeightUpToThirtyKg_ReturnsZero(string weight)
    {
        // Arrange
        var policy = CreatePolicy(AfterEffective);

        // Act
        var surcharge = policy.SurchargeFor(Kg(weight), AfterEffective);

        // Assert
        Assert.Equal(0m, surcharge);
    }

    [Theory]
    [InlineData("30.001")]  // first gram over the threshold
    [InlineData("40")]
    [InlineData("50")]      // top of the first tier
    public void SurchargeFor_WeightOverThirtyUpToFiftyKg_ReturnsFifteen(string weight)
    {
        // Arrange
        var policy = CreatePolicy(AfterEffective);

        // Act
        var surcharge = policy.SurchargeFor(Kg(weight), AfterEffective);

        // Assert
        Assert.Equal(15.00m, surcharge);
    }

    [Theory]
    [InlineData("50.001")]  // first gram of the second tier
    [InlineData("60")]
    [InlineData("70")]      // heaviest allowed parcel
    public void SurchargeFor_WeightOverFiftyUpToSeventyKg_ReturnsTwentyFive(string weight)
    {
        // Arrange
        var policy = CreatePolicy(AfterEffective);

        // Act
        var surcharge = policy.SurchargeFor(Kg(weight), AfterEffective);

        // Assert
        Assert.Equal(25.00m, surcharge);
    }

    [Theory]
    [InlineData("70.001")]
    [InlineData("0.099")]
    [InlineData("0")]
    [InlineData("-5")]
    public void SurchargeFor_WeightOutsideServiceRange_ThrowsArgumentOutOfRange(string weight)
    {
        // Arrange
        var policy = CreatePolicy(AfterEffective);

        // Act
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => policy.SurchargeFor(Kg(weight), AfterEffective));

        // Assert
        Assert.Equal("weightKg", ex.ParamName);
    }

    [Fact]
    public void SurchargeFor_HeavyShipmentBookedJustBeforeEffectiveDate_ReturnsZero()
    {
        // Arrange
        var policy = CreatePolicy(AfterEffective);

        // Act
        var surcharge = policy.SurchargeFor(45m, EffectiveFrom.AddTicks(-1));

        // Assert
        Assert.Equal(0m, surcharge);
    }

    [Fact]
    public void SurchargeFor_HeavyShipmentBookedExactlyAtEffectiveDate_AppliesSurcharge()
    {
        // Arrange
        var policy = CreatePolicy(AfterEffective);

        // Act
        var surcharge = policy.SurchargeFor(45m, EffectiveFrom);

        // Assert
        Assert.Equal(15.00m, surcharge);
    }

    [Fact]
    public void QuoteNow_ClockMovesPastEffectiveDate_SurchargeStartsApplying()
    {
        // Arrange: the injected clock (not DateTime.Now) decides what "now" is
        var clock = new FakeTimeProvider(EffectiveFrom.AddMinutes(-1));
        var policy = CreatePolicy(clock);

        // Act
        var before = policy.QuoteNow(45m);
        clock.Advance(TimeSpan.FromMinutes(1));
        var after = policy.QuoteNow(45m);

        // Assert
        Assert.Equal(0m, before);
        Assert.Equal(15.00m, after);
    }

    [Fact]
    public void QuoteNow_CustomEffectiveDateFromOptions_IsHonoured()
    {
        // Arrange: configuration, not a constant, decides the start date
        var customStart = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var options = Options.Create(new HeavyParcelSurchargeOptions { EffectiveFromUtc = customStart });
        var policy = new HeavyParcelSurchargePolicy(options, new FakeTimeProvider(customStart.AddDays(-1)));

        // Act
        var surcharge = policy.QuoteNow(45m);

        // Assert
        Assert.Equal(0m, surcharge);
    }
}
