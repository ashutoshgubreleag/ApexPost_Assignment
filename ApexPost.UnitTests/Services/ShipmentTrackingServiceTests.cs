using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Tracking;
using ApexPost.UnitTests.Support;
using Microsoft.Extensions.Time.Testing;

namespace ApexPost.UnitTests.Services;

/// <summary>
/// Risks R2 and R3 (TESTING-STRATEGY.md): duplicate tracking numbers and scan de-duplication, tested as INTERACTIONS
/// with the persistence ports (what is added, saved, discarded, and what is never called).
///
/// What these mocks do NOT prove: that SQL Server really raises a duplicate-key error, that ExistsAsync really finds the
/// row, or that the LINQ behind the ports translates. That is proven by the SQL Server integration tests.
/// The behaviour-over-state tests (status walks, re-uploading a batch) live in ApexPost.Api.Tests with a stateful fake.
/// </summary>
public sealed class ShipmentTrackingServiceTests
{
    private const string Tn = TrackingTestData.TrackingNumber;

    private readonly Mock<ITrackedShipmentRepository> _repository = new();
    private readonly Mock<ITrackingQueries> _queries = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly FakeTimeProvider _clock = new(TrackingTestData.Now);

    private ShipmentTrackingService CreateService() => new(_repository.Object, _queries.Object, _unitOfWork.Object, _clock);

    // ---------------------------------------------------------------- create

    [Fact]
    public async Task CreateAsync_ValidCommand_AddsCreatedShipmentAndSavesOnce()
    {
        // Arrange
        _repository.Setup(r => r.ExistsAsync(Tn, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _queries.Setup(q => q.GetAsync(Tn, It.IsAny<CancellationToken>())).ReturnsAsync(TrackingTestData.Dto());
        var service = CreateService();

        // Act
        var created = await service.CreateAsync(TrackingTestData.ValidCommand(), CancellationToken.None);

        // Assert
        Assert.Equal(TrackingStatus.Created, created.Status);
        _repository.Verify(r => r.Add(It.Is<TrackedShipment>(s =>
            s.TrackingNumber == Tn
            && s.Status == TrackingStatus.Created
            && s.WeightKg == 2.5m
            && s.CreatedAt == TrackingTestData.Now)), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_InvalidCommand_NeverTouchesPersistence()
    {
        // Arrange: Strict mocks throw on ANY call, so the test fails if validation lets the command through.
        var repository = new Mock<ITrackedShipmentRepository>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var service = new ShipmentTrackingService(repository.Object, _queries.Object, unitOfWork.Object, _clock);

        // Act
        await Assert.ThrowsAsync<CommandValidationException>(
            () => service.CreateAsync(TrackingTestData.ValidCommand(weightKg: 0m), CancellationToken.None));

        // Assert
        repository.VerifyNoOtherCalls();
        unitOfWork.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_TrackingNumberAlreadyExists_ThrowsConflictAndNeverAdds()
    {
        // Arrange
        _repository.Setup(r => r.ExistsAsync(Tn, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = CreateService();

        // Act
        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(TrackingTestData.ValidCommand(), CancellationToken.None));

        // Assert
        Assert.Equal("DUPLICATE_TRACKING_NUMBER", ex.ErrorCode);
        _repository.Verify(r => r.Add(It.IsAny<TrackedShipment>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_UniqueIndexWinsRace_DiscardsChangesAndThrowsConflict()
    {
        // Arrange: the existence check passes, but another request inserted the same number before our SaveChanges.
        _repository.Setup(r => r.ExistsAsync(Tn, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateKeyException("UQ_TrackedShipments_TrackingNumber"));
        var service = CreateService();

        // Act
        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(TrackingTestData.ValidCommand(), CancellationToken.None));

        // Assert
        Assert.Equal("DUPLICATE_TRACKING_NUMBER", ex.ErrorCode);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Once);
        _queries.Verify(q => q.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_CallerToken_IsPassedToEveryPort()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        _repository.Setup(r => r.ExistsAsync(Tn, token)).ReturnsAsync(false);
        _unitOfWork.Setup(u => u.SaveChangesAsync(token)).ReturnsAsync(1);
        _queries.Setup(q => q.GetAsync(Tn, token)).ReturnsAsync(TrackingTestData.Dto());
        var service = CreateService();

        // Act
        await service.CreateAsync(TrackingTestData.ValidCommand(), token);

        // Assert: cancellation must reach the database calls, otherwise an abandoned request keeps running
        _repository.Verify(r => r.ExistsAsync(Tn, token), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(token), Times.Once);
        _queries.Verify(q => q.GetAsync(Tn, token), Times.Once);
    }

    // ---------------------------------------------------------------- read / status / delete

    [Fact]
    public async Task GetAsync_UnknownTrackingNumber_ThrowsEntityNotFound()
    {
        // Arrange
        _queries.Setup(q => q.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((TrackedShipmentDto?)null);
        var service = CreateService();

        // Act
        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(() => service.GetAsync("APX-99999999", CancellationToken.None));

        // Assert
        Assert.Equal("Shipment", ex.EntityName);
    }

    [Fact]
    public async Task ChangeStatusAsync_LegalTransition_SavesOnce()
    {
        // Arrange
        var shipment = TrackingTestData.Shipment(TrackingStatus.Created);
        _repository.Setup(r => r.GetByTrackingNumberAsync(Tn, It.IsAny<CancellationToken>())).ReturnsAsync(shipment);
        _queries.Setup(q => q.GetAsync(Tn, It.IsAny<CancellationToken>())).ReturnsAsync(TrackingTestData.Dto(status: TrackingStatus.PickedUp));
        var service = CreateService();

        // Act
        await service.ChangeStatusAsync(Tn, TrackingStatus.PickedUp, CancellationToken.None);

        // Assert
        Assert.Equal(TrackingStatus.PickedUp, shipment.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeStatusAsync_IllegalTransition_ThrowsConflictAndNeverSaves()
    {
        // Arrange
        _repository.Setup(r => r.GetByTrackingNumberAsync(Tn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TrackingTestData.Shipment(TrackingStatus.Created));
        var service = CreateService();

        // Act
        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.ChangeStatusAsync(Tn, TrackingStatus.Delivered, CancellationToken.None));

        // Assert
        Assert.Equal("INVALID_STATUS_TRANSITION", ex.ErrorCode);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeStatusAsync_UnknownShipment_ThrowsEntityNotFoundAndNeverSaves()
    {
        // Arrange
        _repository.Setup(r => r.GetByTrackingNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((TrackedShipment?)null);
        var service = CreateService();

        // Act
        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.ChangeStatusAsync(Tn, TrackingStatus.PickedUp, CancellationToken.None));

        // Assert
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ShipmentPastCreated_ThrowsConflictAndNeverRemoves()
    {
        // Arrange
        _repository.Setup(r => r.GetByTrackingNumberAsync(Tn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TrackingTestData.Shipment(TrackingStatus.InTransit));
        var service = CreateService();

        // Act
        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(Tn, CancellationToken.None));

        // Assert
        Assert.Equal("SHIPMENT_NOT_DELETABLE", ex.ErrorCode);
        _repository.Verify(r => r.Remove(It.IsAny<TrackedShipment>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_CreatedShipment_RemovesThatShipmentAndSaves()
    {
        // Arrange
        var shipment = TrackingTestData.Shipment(TrackingStatus.Created);
        _repository.Setup(r => r.GetByTrackingNumberAsync(Tn, It.IsAny<CancellationToken>())).ReturnsAsync(shipment);
        var service = CreateService();

        // Act
        await service.DeleteAsync(Tn, CancellationToken.None);

        // Assert
        _repository.Verify(r => r.Remove(shipment), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------- scan upload

    [Fact]
    public async Task UploadScansAsync_RepeatedScanIdInBatch_LooksUpDistinctIdsOnly()
    {
        // Arrange
        var repeated = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var other = Guid.Parse("22222222-2222-2222-2222-222222222222");
        _repository.Setup(r => r.GetByTrackingNumberAsync(Tn, It.IsAny<CancellationToken>())).ReturnsAsync(TrackingTestData.Shipment(TrackingStatus.PickedUp));
        _repository.Setup(r => r.GetScanOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, int>());
        var service = CreateService();
        ScanInput[] scans = [TrackingTestData.Scan(scanId: repeated), TrackingTestData.Scan(scanId: repeated), TrackingTestData.Scan(scanId: other)];

        // Act
        var result = await service.UploadScansAsync(Tn, scans, CancellationToken.None);

        // Assert
        _repository.Verify(r => r.GetScanOwnersAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(repeated) && ids.Contains(other)),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, result.Accepted);
        Assert.Equal(1, result.DuplicatesSkipped);
    }

    [Fact]
    public async Task UploadScansAsync_EveryScanAlreadyStoredForThisShipment_CountsDuplicatesAndNeverSaves()
    {
        // Arrange: both scan ids already belong to shipment 7, the one being uploaded to (a retried batch).
        var shipment = TrackingTestData.Shipment(TrackingStatus.PickedUp, id: 7);
        ScanInput[] scans = [TrackingTestData.Scan(), TrackingTestData.Scan()];
        _repository.Setup(r => r.GetByTrackingNumberAsync(Tn, It.IsAny<CancellationToken>())).ReturnsAsync(shipment);
        _repository.Setup(r => r.GetScanOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(scans.ToDictionary(s => s.ScanId, _ => 7));
        var service = CreateService();

        // Act
        var result = await service.UploadScansAsync(Tn, scans, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.Accepted);
        Assert.Equal(2, result.DuplicatesSkipped);
        Assert.Empty(result.Rejected);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadScansAsync_ScanIdOwnedByAnotherShipment_RejectsItWithReason()
    {
        // Arrange
        var stolen = TrackingTestData.Scan();
        _repository.Setup(r => r.GetByTrackingNumberAsync(Tn, It.IsAny<CancellationToken>())).ReturnsAsync(TrackingTestData.Shipment(TrackingStatus.PickedUp, id: 7));
        _repository.Setup(r => r.GetScanOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [stolen.ScanId] = 99 });
        var service = CreateService();

        // Act
        var result = await service.UploadScansAsync(Tn, [stolen], CancellationToken.None);

        // Assert
        var rejected = Assert.Single(result.Rejected);
        Assert.Equal(stolen.ScanId, rejected.ScanId);
        Assert.Contains("different shipment", rejected.Reason, StringComparison.Ordinal);
        Assert.Equal(0, result.Accepted);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadScansAsync_DeliveredScanWithoutSignature_RejectsOnlyThatScan()
    {
        // Arrange
        var good = TrackingTestData.Scan(ScanType.Arrived, minutesAfterNow: -120);
        var unsigned = TrackingTestData.Scan(ScanType.Delivered, minutesAfterNow: -60, signature: null);
        _repository.Setup(r => r.GetByTrackingNumberAsync(Tn, It.IsAny<CancellationToken>())).ReturnsAsync(TrackingTestData.Shipment(TrackingStatus.PickedUp));
        _repository.Setup(r => r.GetScanOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, int>());
        var service = CreateService();

        // Act
        var result = await service.UploadScansAsync(Tn, [good, unsigned], CancellationToken.None);

        // Assert
        Assert.Equal(1, result.Accepted);
        Assert.Equal(unsigned.ScanId, Assert.Single(result.Rejected).ScanId);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadScansAsync_RaceOnScanIdIndex_RetriesOneByOneAndCountsTheLoserAsDuplicate()
    {
        // Arrange: the batch save hits the unique ScanId index (another request stored one scan first).
        // The service then retries scan by scan, on a freshly loaded shipment each time: first succeeds, second loses.
        var first = TrackingTestData.Scan(ScanType.Arrived, minutesAfterNow: -120);
        var second = TrackingTestData.Scan(ScanType.Departed, minutesAfterNow: -60);
        _repository.Setup(r => r.GetByTrackingNumberAsync(Tn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => TrackingTestData.Shipment(TrackingStatus.PickedUp));
        _repository.Setup(r => r.GetScanOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, int>());
        _unitOfWork.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateKeyException("UQ_ShipmentScans_ScanId"))  // whole batch
            .ReturnsAsync(1)                                                     // first, alone
            .ThrowsAsync(new DuplicateKeyException("UQ_ShipmentScans_ScanId")); // second, alone
        var service = CreateService();

        // Act
        var result = await service.UploadScansAsync(Tn, [first, second], CancellationToken.None);

        // Assert
        Assert.Equal(1, result.Accepted);
        Assert.Equal(1, result.DuplicatesSkipped);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
        // Discard before each single-scan retry (2) plus once after the losing save (1).
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Exactly(3));
    }

    [Fact]
    public async Task UploadScansAsync_UnknownShipment_ThrowsEntityNotFoundBeforeLookingUpScans()
    {
        // Arrange
        _repository.Setup(r => r.GetByTrackingNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((TrackedShipment?)null);
        var service = CreateService();

        // Act
        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.UploadScansAsync(Tn, [TrackingTestData.Scan()], CancellationToken.None));

        // Assert
        _repository.Verify(r => r.GetScanOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_PageSizeAboveMaximum_IsClampedBeforeReachingTheQuery()
    {
        // Arrange
        _queries.Setup(q => q.ListAsync(It.IsAny<TrackingListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<TrackedShipmentDto>([], 1, TrackingLimits.MaxPageSize, 0));
        var service = CreateService();

        // Act
        await service.ListAsync(new TrackingListQuery(Page: 0, PageSize: 500, City: "  Melbourne "), CancellationToken.None);

        // Assert
        _queries.Verify(q => q.ListAsync(
            It.Is<TrackingListQuery>(x => x.Page == 1 && x.PageSize == TrackingLimits.MaxPageSize && x.City == "Melbourne"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
