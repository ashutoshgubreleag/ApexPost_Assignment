using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Events;
using ApexPost.Services.Inventory;
using ApexPost.Services.MoneyOrders;
using ApexPost.Services.Shipping;
using ApexPost.Services.Transactions;
using ApexPost.Tests.Support;

namespace ApexPost.Tests;

/// <summary>
/// The four rule areas the brief requires unit tests for: stock, money order identity check,
/// parcel weight limits and pricing. These run against the real services and JSON repositories.
/// </summary>
public sealed class StockRuleTests
{
    [Fact]
    public async Task Sale_FailsAndChangesNothing_WhenStockIsInsufficient()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var inventory = harness.Scoped<IInventoryService>();
        var branch = new BranchCode("BR-001");
        var sku = new Sku("PKG-001");
        var other = new Sku("PKG-002");
        var onHand = await inventory.GetAvailableQuantityAsync(branch, sku, CancellationToken.None);
        var otherBefore = await inventory.GetAvailableQuantityAsync(branch, other, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            inventory.DeductAsync(branch, [new StockQuantity(other, 1), new StockQuantity(sku, onHand + 1)], CancellationToken.None));

        Assert.Equal("STOCK_INSUFFICIENT", ex.RuleCode);
        Assert.Equal(onHand, await inventory.GetAvailableQuantityAsync(branch, sku, CancellationToken.None));
        Assert.Equal(otherBefore, await inventory.GetAvailableQuantityAsync(branch, other, CancellationToken.None)); // all or nothing
    }

    [Fact]
    public async Task Sale_CanTakeStockToExactlyZero()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var inventory = harness.Scoped<IInventoryService>();
        var branch = new BranchCode("BR-002");
        var sku = new Sku("OFF-001");
        var onHand = await inventory.GetAvailableQuantityAsync(branch, sku, CancellationToken.None);

        await inventory.DeductAsync(branch, [new StockQuantity(sku, onHand)], CancellationToken.None);

        Assert.Equal(0, await inventory.GetAvailableQuantityAsync(branch, sku, CancellationToken.None));
    }

    [Fact]
    public void StockEntity_RefusesNegativeQuantity()
    {
        var row = BranchStock.Create(new BranchCode("BR-001"), new Sku("PKG-001"), 1, 5, DateTimeOffset.UtcNow);

        var ex = Assert.Throws<BusinessRuleViolationException>(() => row.Remove(2, DateTimeOffset.UtcNow));

        Assert.Equal("STOCK_INSUFFICIENT", ex.RuleCode);
        Assert.Equal(1, row.QuantityOnHand);
        Assert.Equal("STOCK_NEGATIVE", Assert.Throws<BusinessRuleViolationException>(() => BranchStock.Create(new BranchCode("BR-001"), new Sku("PKG-001"), -1, 5, DateTimeOffset.UtcNow)).RuleCode);
    }

    [Fact]
    public async Task ConcurrentSales_OfTheLastItems_NeverOversell()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var branch = new BranchCode("BR-003");
        var sku = new Sku("STN-001");
        var onHand = await harness.Scoped<IInventoryService>().GetAvailableQuantityAsync(branch, sku, CancellationToken.None);

        // Twice as many single-unit sales as there is stock, all at once.
        var attempts = Enumerable.Range(0, onHand * 2).Select(async _ =>
        {
            try
            {
                await harness.Scoped<IInventoryService>().DeductAsync(branch, [new StockQuantity(sku, 1)], CancellationToken.None);
                return true;
            }
            catch (BusinessRuleViolationException)
            {
                return false;
            }
        });
        var results = await Task.WhenAll(attempts);

        Assert.Equal(onHand, results.Count(ok => ok));
        Assert.Equal(0, await harness.Scoped<IInventoryService>().GetAvailableQuantityAsync(branch, sku, CancellationToken.None));
    }

    [Fact]
    public async Task Sale_RaisesStockLowEvent_WhenCrossingReorderLevel()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var events = harness.Get<IApexPostEvents>();
        var raised = new List<StockLowEventArgs>();
        events.StockLow += (_, e) => raised.Add(e);

        var inventory = harness.Scoped<IInventoryService>();
        var stock = await inventory.GetStockForBranchAsync(new BranchCode("BR-004"), CancellationToken.None);
        var healthy = stock.First(s => s.QuantityOnHand > Math.Max(s.ReorderThreshold, 5) + 1);
        var take = healthy.QuantityOnHand - healthy.ReorderThreshold;

        await inventory.DeductAsync(healthy.BranchCode, [new StockQuantity(healthy.Sku, take)], CancellationToken.None);

        var e = Assert.Single(raised);
        Assert.Equal(healthy.Sku, e.Sku);
    }
}

public sealed class MoneyOrderIdentityRuleTests
{
    [Fact]
    public async Task Payment_IsBlocked_ForHighValueMoneyOrderWithoutVerification()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var service = harness.Scoped<ITransactionService>();
        var clerk = ServiceHarness.Clerk();
        var tx = await service.OpenAsync(clerk, 1, CancellationToken.None);
        await service.AddMoneyOrderAsync(tx.Id, new MoneyOrderCommand { Amount = 1500m, Sender = "Ann", Recipient = "Ben" }, clerk, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.PayAsync(tx.Id, PaymentMethod.Cash, clerk, CancellationToken.None));

        Assert.Equal("MONEY_ORDER_ID_REQUIRED", ex.RuleCode);
    }

    [Fact]
    public async Task Payment_IsBlocked_WhenTheVerificationFailed()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var service = harness.Scoped<ITransactionService>();
        var clerk = ServiceHarness.Clerk();
        var tx = await service.OpenAsync(clerk, 1, CancellationToken.None);
        await service.AddMoneyOrderAsync(tx.Id, new MoneyOrderCommand { Amount = 5000m, Sender = "Ann", Recipient = "Ben" }, clerk, CancellationToken.None);
        await service.AddIdentityVerificationAsync(tx.Id, Verification(VerificationResult.Failed), clerk, CancellationToken.None);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.PayAsync(tx.Id, PaymentMethod.Card, clerk, CancellationToken.None));
    }

    [Fact]
    public async Task Payment_Succeeds_WithPassedVerificationInTheSameTransaction_AndLinksIt()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var service = harness.Scoped<ITransactionService>();
        var clerk = ServiceHarness.Clerk();
        var tx = await service.OpenAsync(clerk, 1, CancellationToken.None);
        var order = await service.AddMoneyOrderAsync(tx.Id, new MoneyOrderCommand { Amount = 1500m, Sender = "Ann", Recipient = "Ben" }, clerk, CancellationToken.None);
        var check = await service.AddIdentityVerificationAsync(tx.Id, Verification(VerificationResult.Passed), clerk, CancellationToken.None);

        var paid = await service.PayAsync(tx.Id, PaymentMethod.Card, clerk, CancellationToken.None);

        Assert.Equal(TransactionStatus.Paid, paid.Status);
        var issued = await harness.Scoped<IMoneyOrderRepository>().GetByIdAsync(order.Id, CancellationToken.None);
        Assert.Equal(MoneyOrderStatus.Issued, issued!.Status);
        Assert.Equal(check.Id, issued.IdentityVerificationId);
    }

    [Fact]
    public async Task VerificationInAnotherTransaction_DoesNotCount()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var service = harness.Scoped<ITransactionService>();
        var clerk = ServiceHarness.Clerk();
        var first = await service.OpenAsync(clerk, 1, CancellationToken.None);
        await service.AddIdentityVerificationAsync(first.Id, Verification(VerificationResult.Passed), clerk, CancellationToken.None);
        var second = await service.OpenAsync(clerk, 1, CancellationToken.None);
        await service.AddMoneyOrderAsync(second.Id, new MoneyOrderCommand { Amount = 2000m, Sender = "Ann", Recipient = "Ben" }, clerk, CancellationToken.None);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.PayAsync(second.Id, PaymentMethod.Card, clerk, CancellationToken.None));
    }

    [Theory]
    [InlineData(999.99, false)]
    [InlineData(1000.00, false)] // "above" the threshold, so exactly 1000 does not need a check
    [InlineData(1000.01, true)]
    public async Task Threshold_IsConfigurableAndExclusive(decimal amount, bool requiresCheck)
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var rules = harness.Get<ApexPost.Services.Common.BusinessRules>();

        Assert.Equal(requiresCheck, rules.RequiresIdentityVerification(new Money(amount)));
    }

    [Fact]
    public async Task Threshold_ComesFromConfiguration()
    {
        await using var harness = await ServiceHarness.CreateAsync(new Dictionary<string, string?> { ["BusinessRules:MoneyOrderIdentityThreshold"] = "200" });
        var rules = harness.Get<ApexPost.Services.Common.BusinessRules>();

        Assert.True(rules.RequiresIdentityVerification(new Money(250m)));
    }

    [Fact]
    public async Task DocumentNumber_IsStoredMasked_NeverInFull()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var service = harness.Scoped<ITransactionService>();
        var clerk = ServiceHarness.Clerk();
        var tx = await service.OpenAsync(clerk, 1, CancellationToken.None);

        var check = await service.AddIdentityVerificationAsync(tx.Id, Verification(VerificationResult.Passed, "PA 9876 5432"), clerk, CancellationToken.None);

        Assert.Equal("****5432", check.MaskedDocumentNumber.Value);
        Assert.False(await harness.StorageContainsAsync("98765432"));
        Assert.False(await harness.StorageContainsAsync("PA 9876"));
    }

    private static IdentityVerificationCommand Verification(VerificationResult result, string number = "N1234567") => new()
    {
        CustomerName = "Ann Customer",
        DocumentType = DocumentType.Passport,
        DocumentNumber = number,
        Result = result,
    };
}

public sealed class ParcelWeightLimitTests
{
    [Theory]
    [InlineData(ShippingServiceLevel.Standard, 22.0, true)]
    [InlineData(ShippingServiceLevel.Standard, 22.01, false)]
    [InlineData(ShippingServiceLevel.Express, 20.0, true)]
    [InlineData(ShippingServiceLevel.Express, 20.5, false)]
    [InlineData(ShippingServiceLevel.Registered, 5.0, true)]
    [InlineData(ShippingServiceLevel.Registered, 5.1, false)]
    public void Parcel_IsRejected_AboveTheServiceLevelMaximum(ShippingServiceLevel level, double kg, bool accepted)
    {
        var act = () => ServiceLevelRules.EnsureAcceptable(ShippingType.Parcel, level, new Weight((decimal)kg), new Dimensions(30, 20, 10));

        if (accepted)
        {
            act();
        }
        else
        {
            Assert.Equal("PARCEL_OVERWEIGHT", Assert.Throws<BusinessRuleViolationException>(act).RuleCode);
        }
    }

    [Fact]
    public void Letter_OverHalfAKilo_IsRejected()
    {
        var ex = Assert.Throws<BusinessRuleViolationException>(() =>
            ServiceLevelRules.EnsureAcceptable(ShippingType.Letter, ShippingServiceLevel.Standard, new Weight(0.6m), new Dimensions(24, 16, 1)));
        Assert.Equal("PARCEL_OVERWEIGHT", ex.RuleCode);
    }

    [Fact]
    public void ThickLetter_MustGoAsParcel()
    {
        var ex = Assert.Throws<BusinessRuleViolationException>(() =>
            ServiceLevelRules.EnsureAcceptable(ShippingType.Letter, ShippingServiceLevel.Standard, new Weight(0.2m), new Dimensions(24, 16, 3)));
        Assert.Equal("LETTER_OVERSIZE", ex.RuleCode);
    }

    [Fact]
    public async Task OverweightParcel_CannotBeAddedToABasket()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var service = harness.Scoped<ITransactionService>();
        var clerk = ServiceHarness.Clerk();
        var tx = await service.OpenAsync(clerk, 1, CancellationToken.None);
        var command = new AddShipmentCommand(
            new ShippingQuoteCommand { ShippingType = ShippingType.Parcel, ServiceLevel = ShippingServiceLevel.Registered, ZoneCode = "Z01", WeightKg = 6m, LengthCm = 30, WidthCm = 20, HeightCm = 10 },
            new RecipientCommand { RecipientName = "A", Line1 = "1 St", City = "Sydney", State = "NSW", Postcode = "2000" });

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.AddShipmentAsync(tx.Id, command, clerk, CancellationToken.None));
    }
}

public sealed class PricingTests
{
    private static readonly ZoneDto Metro = new("Z01", "Metro", IsInternational: false, IsRemote: false, LetterBaseRate: new Money(1.20m), ParcelBaseRate: new Money(8.95m), PerKgRate: new Money(1.10m));
    private static readonly ZoneDto Remote = new("Z05", "Remote", IsInternational: false, IsRemote: true, LetterBaseRate: new Money(1.40m), ParcelBaseRate: new Money(16.95m), PerKgRate: new Money(3.20m));
    private static readonly ZoneDto Europe = new("Z09", "Europe", IsInternational: true, IsRemote: false, LetterBaseRate: new Money(4.20m), ParcelBaseRate: new Money(34.95m), PerKgRate: new Money(9.40m));

    private readonly DefaultShippingPricingStrategy _strategy = new();

    [Fact]
    public void StandardDomesticParcel_BaseFuelAndTax()
    {
        // 2 kg, small box: base 8.95 + 1.10 x ceil(1.5) = 11.15; fuel 8% = 0.89; net 12.04; GST 10% = 1.20.
        var quote = _strategy.Price(Context(Metro, ShippingType.Parcel, ShippingServiceLevel.Standard, 2m, new Dimensions(20, 15, 10), 0.08m));

        Assert.Equal(new Money(11.15m), quote.BasePostage);
        Assert.Equal(new Money(12.04m), quote.Net);
        Assert.Equal(TaxCategory.Standard, quote.TaxCategory);
        Assert.Equal(new Money(1.20m), quote.Tax);
        Assert.Equal(new Money(13.24m), quote.Total);
    }

    [Fact]
    public void Express_Adds60PercentOfBase()
    {
        var standard = _strategy.Price(Context(Metro, ShippingType.Parcel, ShippingServiceLevel.Standard, 2m, new Dimensions(20, 15, 10), 0m));
        var express = _strategy.Price(Context(Metro, ShippingType.Parcel, ShippingServiceLevel.Express, 2m, new Dimensions(20, 15, 10), 0m));

        Assert.Equal(new Money(6.69m), express.ServiceLoading);
        Assert.Equal(standard.Net + new Money(6.69m), express.Net);
    }

    [Fact]
    public void BulkyParcel_IsChargedOnVolumetricWeight()
    {
        // 60x40x30 cm = 72,000 cm³ / 5,000 = 14.4 kg volumetric, far above the 2 kg actual weight.
        var quote = _strategy.Price(Context(Metro, ShippingType.Parcel, ShippingServiceLevel.Standard, 2m, new Dimensions(60, 40, 30), 0m));

        Assert.Equal(new Weight(14.4m), quote.ChargeableWeight);
        Assert.Equal(new Money(8.95m + 1.10m * 14), quote.BasePostage);
    }

    [Fact]
    public void International_IsTaxExempt_AndRemoteAddsSurcharge()
    {
        var europe = _strategy.Price(Context(Europe, ShippingType.Parcel, ShippingServiceLevel.Registered, 1m, new Dimensions(20, 15, 10), 0.12m));
        var remote = _strategy.Price(Context(Remote, ShippingType.Letter, ShippingServiceLevel.Standard, 0.1m, new Dimensions(22, 11, 0.5m), 0m));

        Assert.Equal(TaxCategory.Exempt, europe.TaxCategory);
        Assert.Equal(Money.Zero, europe.Tax);
        Assert.Equal(new Money(4.50m), europe.ServiceLoading);
        Assert.Contains(remote.Surcharges, s => s.Name == "Remote area" && s.Amount == new Money(3.00m));
    }

    [Fact]
    public void Surcharges_ArePluggableDelegates()
    {
        ShippingSurchargeRule christmas = _ => new SurchargeLine("Peak season", new Money(1.00m));
        var strategy = new DefaultShippingPricingStrategy([christmas]);

        var quote = strategy.Price(Context(Metro, ShippingType.Letter, ShippingServiceLevel.Standard, 0.1m, new Dimensions(22, 11, 0.5m), 0.08m));

        Assert.Equal(new Money(2.20m), quote.Net); // 1.20 base + 1.00 peak, no fuel rule registered
    }

    [Fact]
    public async Task Basket_TaxAndTotals_MixRetailAndServices()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var service = harness.Scoped<ITransactionService>();
        var clerk = ServiceHarness.Clerk();
        var inventory = harness.Scoped<IInventoryService>();
        foreach (var sku in new[] { "STN-004", "PKG-001" })
        {
            await inventory.AdjustAsync(new StockAdjustmentCommand { BranchCode = "BR-001", Sku = sku, QuantityChange = 10, Reason = "Test delivery" }, ServiceHarness.Manager(), CancellationToken.None);
        }

        var tx = await service.OpenAsync(clerk, 1, CancellationToken.None);
        await service.AddRetailItemAsync(tx.Id, new AddRetailItemCommand { Sku = "STN-004", Quantity = 2 }, clerk, CancellationToken.None); // 4.95 Exempt
        await service.AddRetailItemAsync(tx.Id, new AddRetailItemCommand { Sku = "PKG-001", Quantity = 1 }, clerk, CancellationToken.None); // 2.95 Standard
        var order = await service.AddMoneyOrderAsync(tx.Id, new MoneyOrderCommand { Amount = 200m, Sender = "A", Recipient = "B" }, clerk, CancellationToken.None);
        var details = await service.GetDetailsAsync(tx.Id, clerk, CancellationToken.None);
        var t = details.Transaction;

        Assert.Equal(new Money(5.00m), order.Fee);                    // minimum fee
        Assert.Equal(new Money(12.85m), details.RetailNet);                 // 9.90 + 2.95
        Assert.Equal(new Money(5.00m), details.ServicesNet);
        Assert.Equal(new Money(0.30m), t.TaxAmount);                  // only the 2.95 line is taxed
        Assert.Equal(new Money(200m), t.PassThroughAmount);
        Assert.Equal(new Money(218.15m), t.TotalAmount);
    }

    private static ShippingPricingContext Context(ZoneDto zone, ShippingType type, ShippingServiceLevel level, decimal kg, Dimensions dims, decimal fuel) =>
        new(type, level, zone, new Weight(kg), dims, fuel);
}
