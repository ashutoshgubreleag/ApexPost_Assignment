using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Tests.Support.Fakes;

namespace ApexPost.Tests.Support;

/// <summary>Small builders for test data that the reference seed does not contain.</summary>
public static class TestData
{
    /// <summary>Creates a paid basket with one lodged parcel in the store and returns its tracking number.</summary>
    public static Task<string> LodgedShipmentAsync(InMemoryStore store)
    {
        var at = DateTimeOffset.UtcNow.AddDays(-1);
        var session = new FakeSession(store);
        var tx = CounterTransaction.Open(new BranchCode("BR-001"), 1, 7, at);
        tx.AddServiceLine(TransactionLineType.Shipping, "APX000000001", "Parcel Standard to Metro", new Money(10m), TaxCategory.Standard, new Money(1m), Money.Zero);
        tx.Pay(PaymentMethod.Cash, at);
        session.Add(tx);
        session.Commit();

        var shipment = Shipment.Create(tx.Id, tx.BranchCode, ShippingType.Parcel, ShippingServiceLevel.Standard, "Z01", new Weight(1m), new Dimensions(30, 20, 10), new Weight(1.2m),
            new Address("Ann Example", "1 Test St", "Sydney", "NSW", "2000"), new Money(10m), at, Random.Shared.Next(100000, 999999));
        shipment.Lodge();
        session.Add(shipment);
        session.Commit();
        return Task.FromResult(shipment.TrackingNumber);
    }
}
