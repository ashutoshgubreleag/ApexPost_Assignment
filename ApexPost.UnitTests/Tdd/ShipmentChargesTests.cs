using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Tracking;
using ApexPost.UnitTests.Support;
using Microsoft.Extensions.Time.Testing;

namespace ApexPost.UnitTests.Tdd;

/// <summary>
/// Week 5 TDD rule, service side: GetChargesAsync asks the injected surcharge policy using the shipment's stored
/// weight and BOOKING time (not "now"), so a shipment booked before the effective date never starts paying later.
/// Written BEFORE the implementation existed (RED commit).
/// </summary>
public sealed class ShipmentChargesTests
{
    private readonly Mock<ITrackingQueries> _queries = new();
    private readonly Mock<IShipmentSurchargePolicy> _policy = new();

    private ShipmentTrackingService CreateService() => new(
        Mock.Of<ITrackedShipmentRepository>(), _queries.Object, Mock.Of<IUnitOfWork>(), new FakeTimeProvider(TrackingTestData.Now), _policy.Object);

    [Fact]
    public async Task GetChargesAsync_HeavyShipment_ReturnsThePolicysSurchargeForItsWeightAndBookingTime()
    {
        // Arrange
        var bookedAt = new DateTimeOffset(2026, 12, 5, 9, 0, 0, TimeSpan.Zero);
        _queries.Setup(q => q.GetAsync(TrackingTestData.TrackingNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TrackingTestData.Dto() with { WeightKg = 45m, CreatedAt = bookedAt });
        _policy.Setup(p => p.SurchargeFor(45m, bookedAt)).Returns(15.00m);
        var service = CreateService();

        // Act
        var charges = await service.GetChargesAsync(TrackingTestData.TrackingNumber, CancellationToken.None);

        // Assert
        Assert.Equal(new ShipmentChargesDto(TrackingTestData.TrackingNumber, 45m, 15.00m), charges);
        _policy.Verify(p => p.SurchargeFor(45m, bookedAt), Times.Once);
        _policy.Verify(p => p.QuoteNow(It.IsAny<decimal>()), Times.Never); // booking time, not "now"
    }

    [Fact]
    public async Task GetChargesAsync_UnknownShipment_ThrowsEntityNotFoundAndNeverAsksThePolicy()
    {
        // Arrange
        _queries.Setup(q => q.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((TrackedShipmentDto?)null);
        var service = CreateService();

        // Act
        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.GetChargesAsync("APX-99999999", CancellationToken.None));

        // Assert
        _policy.Verify(p => p.SurchargeFor(It.IsAny<decimal>(), It.IsAny<DateTimeOffset>()), Times.Never);
    }
}
