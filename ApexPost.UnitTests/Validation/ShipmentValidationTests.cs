using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Tracking;
using ApexPost.UnitTests.Support;
using Microsoft.Extensions.Time.Testing;

namespace ApexPost.UnitTests.Validation;

/// <summary>
/// Risk R7 (TESTING-STRATEGY.md): validation boundaries. Every limit is tested just inside, on and just outside.
/// Invalid input must be rejected before persistence is touched, so the persistence mocks are Strict:
/// any unexpected call fails the test.
/// </summary>
public sealed class ShipmentValidationTests
{
    private readonly Mock<ITrackedShipmentRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<ITrackingQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly ShipmentTrackingService _service;

    public ShipmentValidationTests() =>
        _service = new ShipmentTrackingService(_repository.Object, _queries.Object, _unitOfWork.Object, new FakeTimeProvider(TrackingTestData.Now), Mock.Of<IShipmentSurchargePolicy>());

    private void AllowSuccessfulCreate()
    {
        _repository.Setup(r => r.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repository.Setup(r => r.Add(It.IsAny<TrackedShipment>()));
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _queries.Setup(q => q.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tn, CancellationToken _) => TrackingTestData.Dto(tn));
    }

    [Theory]
    [InlineData("0.099")]   // just below the minimum
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("70.001")]  // just above the maximum
    [InlineData("1000")]
    public async Task CreateAsync_WeightOutsideRange_ThrowsValidationForWeightOnly(string weight)
    {
        // Arrange
        var command = TrackingTestData.ValidCommand(weightKg: decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture));

        // Act
        var ex = await Assert.ThrowsAsync<CommandValidationException>(() => _service.CreateAsync(command, CancellationToken.None));

        // Assert
        var error = Assert.Single(ex.Errors);
        Assert.Equal(nameof(ShipmentDetails.WeightKg), error.PropertyName);
    }

    [Theory]
    [InlineData("0.1")]     // exactly the minimum
    [InlineData("0.101")]
    [InlineData("30")]
    [InlineData("69.999")]
    [InlineData("70")]      // exactly the maximum
    public async Task CreateAsync_WeightInsideRange_CreatesShipment(string weight)
    {
        // Arrange
        AllowSuccessfulCreate();
        var command = TrackingTestData.ValidCommand(weightKg: decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture));

        // Act
        var created = await _service.CreateAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal(TrackingTestData.TrackingNumber, created.TrackingNumber);
    }

    [Theory]
    [InlineData("APX-1234567")]     // 7 digits: one short
    [InlineData("APX-123456789")]   // 9 digits: one too many
    [InlineData("apx-12345678")]    // lower-case prefix
    [InlineData("APX12345678")]     // missing dash
    [InlineData("XYZ-12345678")]    // wrong prefix
    [InlineData("APX-1234567A")]    // letter instead of digit
    [InlineData(" APX-12345678")]   // leading space
    [InlineData("")]
    public async Task CreateAsync_MalformedTrackingNumber_ThrowsValidationForTrackingNumber(string trackingNumber)
    {
        // Arrange
        var command = TrackingTestData.ValidCommand(trackingNumber);

        // Act
        var ex = await Assert.ThrowsAsync<CommandValidationException>(() => _service.CreateAsync(command, CancellationToken.None));

        // Assert
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateTrackedShipmentCommand.TrackingNumber));
    }

    [Theory]
    [InlineData("APX-00000000")]
    [InlineData("APX-99999999")]
    public async Task CreateAsync_WellFormedTrackingNumber_CreatesShipment(string trackingNumber)
    {
        // Arrange
        AllowSuccessfulCreate();

        // Act
        var created = await _service.CreateAsync(TrackingTestData.ValidCommand(trackingNumber), CancellationToken.None);

        // Assert
        Assert.Equal(trackingNumber, created.TrackingNumber);
    }

    [Theory]
    [InlineData("12345")]           // too short
    [InlineData("phone-number")]    // letters
    [InlineData("   ")]
    public async Task CreateAsync_InvalidPhone_ThrowsValidationForPhone(string phone)
    {
        // Arrange
        var command = new CreateTrackedShipmentCommand(TrackingTestData.TrackingNumber,
            TrackingTestData.ValidDetails() with { RecipientPhone = phone });

        // Act
        var ex = await Assert.ThrowsAsync<CommandValidationException>(() => _service.CreateAsync(command, CancellationToken.None));

        // Assert
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(ShipmentDetails.RecipientPhone));
    }

    [Fact]
    public async Task CreateAsync_EveryFieldInvalid_ReportsEveryErrorAtOnce()
    {
        // Arrange
        var command = new CreateTrackedShipmentCommand("bad", new ShipmentDetails("", "", "", "", 0m));

        // Act
        var ex = await Assert.ThrowsAsync<CommandValidationException>(() => _service.CreateAsync(command, CancellationToken.None));

        // Assert: one error per field, so the client can fix the whole form in one round trip
        Assert.Equal(
            ["AddressLine", "City", "RecipientName", "RecipientPhone", "TrackingNumber", "WeightKg"],
            ex.Errors.Select(e => e.PropertyName).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(0)]     // below the minimum of 1
    [InlineData(101)]   // just above the maximum of 100
    public async Task UploadScansAsync_BatchSizeOutsideLimits_ThrowsValidation(int count)
    {
        // Arrange
        var scans = Enumerable.Range(0, count).Select(_ => TrackingTestData.Scan()).ToList();

        // Act
        var ex = await Assert.ThrowsAsync<CommandValidationException>(
            () => _service.UploadScansAsync(TrackingTestData.TrackingNumber, scans, CancellationToken.None));

        // Assert
        Assert.Equal("scans", Assert.Single(ex.Errors).PropertyName);
    }

    [Theory]
    [InlineData(1)]     // exactly the minimum
    [InlineData(100)]   // exactly the maximum
    public async Task UploadScansAsync_BatchSizeAtLimits_AcceptsEveryScan(int count)
    {
        // Arrange
        var shipment = TrackingTestData.Shipment(TrackingStatus.PickedUp);
        _repository.Setup(r => r.GetByTrackingNumberAsync(TrackingTestData.TrackingNumber, It.IsAny<CancellationToken>())).ReturnsAsync(shipment);
        _repository.Setup(r => r.GetScanOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(count);
        var scans = Enumerable.Range(0, count).Select(i => TrackingTestData.Scan(minutesAfterNow: -200 + i)).ToList();

        // Act
        var result = await _service.UploadScansAsync(TrackingTestData.TrackingNumber, scans, CancellationToken.None);

        // Assert
        Assert.Equal(count, result.Accepted);
        Assert.Empty(result.Rejected);
    }

    [Theory]
    [InlineData("0.099")]
    [InlineData("70.001")]
    public void TrackedShipmentCreate_WeightOutsideRange_ThrowsArgumentOutOfRange(string weight)
    {
        // Arrange
        var kg = decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture);

        // Act + Assert: the domain guards itself even if a caller skips the service validation
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrackedShipment.Create(TrackingTestData.TrackingNumber, "Test Recipient", "+61 400 000 111", "1 Test Street", "Melbourne", kg, TrackingTestData.Now));
        Assert.Equal("weightKg", ex.ParamName);
    }
}
