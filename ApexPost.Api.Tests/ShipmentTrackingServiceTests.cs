using ApexPost.Api.Tests.Support;
using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Services.Tracking;

namespace ApexPost.Api.Tests;

/// <summary>Business rules of the tracking API, exercised through the service with an in-memory store.</summary>
public sealed class ShipmentTrackingServiceTests
{
    private const string Tn = "APX-10000001";
    private const string OtherTn = "APX-10000002";

    private readonly InMemoryTrackingStore _store = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero));
    private readonly ShipmentTrackingService _service;

    public ShipmentTrackingServiceTests() =>
        _service = new ShipmentTrackingService(_store.Repository, _store.Queries, _store.UnitOfWork, _clock);

    private static ShipmentDetails Details(string city = "Melbourne") =>
        new("Ada Lovelace", "+61 400 000 111", "12 Example Street", city, 2.5m);

    private Task<TrackedShipmentDto> CreateAsync(string trackingNumber = Tn, string city = "Melbourne") =>
        _service.CreateAsync(new CreateTrackedShipmentCommand(trackingNumber, Details(city)), CancellationToken.None);

    private async Task AdvanceToAsync(string trackingNumber, params TrackingStatus[] steps)
    {
        foreach (var step in steps)
        {
            await _service.ChangeStatusAsync(trackingNumber, step, CancellationToken.None);
        }
    }

    private static ScanInput Scan(Guid id, ScanType type, int minutesAgo, string? signature = null) =>
        new(id, type, "Melbourne Depot", new DateTimeOffset(2026, 11, 9, 7, 0, 0, TimeSpan.Zero).AddMinutes(-minutesAgo), signature);

    // ---- create / read ----

    [Fact]
    public async Task Create_StoresShipmentInStatusCreated()
    {
        var created = await CreateAsync();

        Assert.Equal(Tn, created.TrackingNumber);
        Assert.Equal(TrackingStatus.Created, created.Status);
        Assert.Equal(_clock.GetUtcNow(), created.CreatedAt);
        Assert.Null(created.LatestScan);
        Assert.Equal(Tn, (await _service.GetAsync(Tn, CancellationToken.None)).TrackingNumber);
    }

    [Fact]
    public async Task Create_DuplicateTrackingNumber_IsAConflict()
    {
        await CreateAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => CreateAsync());

        Assert.Equal("DUPLICATE_TRACKING_NUMBER", ex.ErrorCode);
    }

    [Fact]
    public async Task Create_WhenTheUniqueIndexWinsTheRace_IsStillAConflict()
    {
        // Another request commits the same tracking number after our ExistsAsync check but before our insert.
        var competing = new InMemoryTrackingStore();
        var service = new ShipmentTrackingService(competing.Repository, competing.Queries, competing.UnitOfWork, _clock);
        competing.BeforeNextCommit = () =>
        {
            var rival = TrackedShipment.Create(Tn, "Rival", "+61 400 000 222", "1 Other Road", "Sydney", 1m, _clock.GetUtcNow());
            competing.Repository.Add(rival);
        };

        // The hook stages a rival insert for the same number inside the same unit of work, so the commit hits the unique index.
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new CreateTrackedShipmentCommand(Tn, Details()), CancellationToken.None));

        Assert.Equal("DUPLICATE_TRACKING_NUMBER", ex.ErrorCode);
    }

    [Theory]
    [InlineData("APX-1234567")]
    [InlineData("APX-123456789")]
    [InlineData("apx-12345678")]
    [InlineData("XYZ-12345678")]
    [InlineData("")]
    public async Task Create_BadTrackingNumber_IsRejectedWithoutWriting(string trackingNumber)
    {
        var ex = await Assert.ThrowsAsync<CommandValidationException>(() => CreateAsync(trackingNumber));

        Assert.Contains(ex.Errors, e => e.PropertyName == "TrackingNumber");
        Assert.Equal(0, _store.Commits);
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(70.1)]
    [InlineData(-1.0)]
    public async Task Create_WeightOutsideRange_IsRejected(double weight)
    {
        var details = Details() with { WeightKg = (decimal)weight };

        var ex = await Assert.ThrowsAsync<CommandValidationException>(() =>
            _service.CreateAsync(new CreateTrackedShipmentCommand(Tn, details), CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == "WeightKg");
    }

    [Fact]
    public async Task Create_MissingRecipientFieldsAndBadPhone_ReportEveryProblem()
    {
        var details = new ShipmentDetails(" ", "not-a-phone", "", " ", 2m);

        var ex = await Assert.ThrowsAsync<CommandValidationException>(() =>
            _service.CreateAsync(new CreateTrackedShipmentCommand(Tn, details), CancellationToken.None));

        var names = ex.Errors.Select(e => e.PropertyName).ToHashSet();
        Assert.Contains("RecipientName", names);
        Assert.Contains("RecipientPhone", names);
        Assert.Contains("AddressLine", names);
        Assert.Contains("City", names);
    }

    [Fact]
    public async Task Get_UnknownTrackingNumber_IsNotFound()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(() => _service.GetAsync(Tn, CancellationToken.None));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => _service.GetScansAsync(Tn, CancellationToken.None));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => _service.DeleteAsync(Tn, CancellationToken.None));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => _service.UpdateAsync(Tn, Details(), CancellationToken.None));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => _service.ChangeStatusAsync(Tn, TrackingStatus.PickedUp, CancellationToken.None));
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _service.UploadScansAsync(Tn, [Scan(Guid.NewGuid(), ScanType.PickedUp, 5)], CancellationToken.None));
    }

    // ---- update ----

    [Fact]
    public async Task Update_ReplacesDetailsWhileCreated()
    {
        await CreateAsync();

        var updated = await _service.UpdateAsync(Tn, new ShipmentDetails("Grace Hopper", "0400 000 333", "9 New Lane", "Sydney", 10m), CancellationToken.None);

        Assert.Equal("Grace Hopper", updated.RecipientName);
        Assert.Equal("Sydney", updated.City);
        Assert.Equal(10m, updated.WeightKg);
        Assert.Equal(TrackingStatus.Created, updated.Status);
    }

    [Fact]
    public async Task Update_AfterPickup_IsAConflict()
    {
        await CreateAsync();
        await AdvanceToAsync(Tn, TrackingStatus.PickedUp);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.UpdateAsync(Tn, Details(), CancellationToken.None));

        Assert.Equal("SHIPMENT_LOCKED", ex.ErrorCode);
    }

    // ---- status machine ----

    [Fact]
    public async Task ChangeStatus_WalksTheWholeHappyPath()
    {
        await CreateAsync();

        await AdvanceToAsync(Tn, TrackingStatus.PickedUp, TrackingStatus.InTransit, TrackingStatus.OutForDelivery, TrackingStatus.Delivered);

        Assert.Equal(TrackingStatus.Delivered, (await _service.GetAsync(Tn, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task ChangeStatus_OutForDeliveryCanFail()
    {
        await CreateAsync();
        await AdvanceToAsync(Tn, TrackingStatus.PickedUp, TrackingStatus.InTransit, TrackingStatus.OutForDelivery);

        var failed = await _service.ChangeStatusAsync(Tn, TrackingStatus.Failed, CancellationToken.None);

        Assert.Equal(TrackingStatus.Failed, failed.Status);
    }

    public static TheoryData<TrackingStatus, TrackingStatus> IllegalMoves => new()
    {
        { TrackingStatus.Created, TrackingStatus.InTransit },
        { TrackingStatus.Created, TrackingStatus.Delivered },
        { TrackingStatus.Created, TrackingStatus.Created },
        { TrackingStatus.PickedUp, TrackingStatus.Created },
        { TrackingStatus.PickedUp, TrackingStatus.OutForDelivery },
        { TrackingStatus.InTransit, TrackingStatus.PickedUp },
        { TrackingStatus.InTransit, TrackingStatus.Delivered },
        { TrackingStatus.OutForDelivery, TrackingStatus.InTransit },
        { TrackingStatus.Delivered, TrackingStatus.Failed },
        { TrackingStatus.Delivered, TrackingStatus.Created },
        { TrackingStatus.Failed, TrackingStatus.Delivered },
        { TrackingStatus.Failed, TrackingStatus.Created },
    };

    [Theory]
    [MemberData(nameof(IllegalMoves))]
    public void CanMoveTo_RejectsEveryIllegalMove(TrackingStatus start, TrackingStatus end) =>
        Assert.False(TrackedShipment.CanMoveTo(start, end));

    [Fact]
    public async Task ChangeStatus_SkippingAStep_IsAConflictAndLeavesTheStatusAlone()
    {
        await CreateAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.ChangeStatusAsync(Tn, TrackingStatus.InTransit, CancellationToken.None));

        Assert.Equal("INVALID_STATUS_TRANSITION", ex.ErrorCode);
        Assert.Equal(TrackingStatus.Created, (await _service.GetAsync(Tn, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task ChangeStatus_UndefinedValue_IsAValidationError() =>
        await Assert.ThrowsAsync<CommandValidationException>(() => _service.ChangeStatusAsync(Tn, (TrackingStatus)99, CancellationToken.None));

    // ---- delete ----

    [Fact]
    public async Task Delete_WhileCreated_RemovesTheShipment()
    {
        await CreateAsync();

        await _service.DeleteAsync(Tn, CancellationToken.None);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => _service.GetAsync(Tn, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_AfterPickup_IsAConflictAndKeepsTheShipment()
    {
        await CreateAsync();
        await AdvanceToAsync(Tn, TrackingStatus.PickedUp);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.DeleteAsync(Tn, CancellationToken.None));

        Assert.Equal("SHIPMENT_NOT_DELETABLE", ex.ErrorCode);
        Assert.Equal(TrackingStatus.PickedUp, (await _service.GetAsync(Tn, CancellationToken.None)).Status);
    }

    // ---- paging and filtering ----

    [Fact]
    public async Task List_ClampsPageSizeToFiftyAndPagesCorrectly()
    {
        for (var i = 0; i < 60; i++)
        {
            await CreateAsync($"APX-{20000000 + i}");
        }

        var first = await _service.ListAsync(new TrackingListQuery(Page: 1, PageSize: 500), CancellationToken.None);
        var second = await _service.ListAsync(new TrackingListQuery(Page: 2, PageSize: 500), CancellationToken.None);

        Assert.Equal(50, first.PageSize);
        Assert.Equal(50, first.Items.Count);
        Assert.Equal(60, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(10, second.Items.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task List_NonPositivePageSize_UsesTheDefaultOfTwenty(int pageSize)
    {
        for (var i = 0; i < 25; i++)
        {
            await CreateAsync($"APX-{30000000 + i}");
        }

        var page = await _service.ListAsync(new TrackingListQuery(PageSize: pageSize), CancellationToken.None);

        Assert.Equal(20, page.PageSize);
        Assert.Equal(20, page.Items.Count);
    }

    [Fact]
    public async Task List_PageBelowOne_IsTreatedAsPageOne()
    {
        await CreateAsync();

        var page = await _service.ListAsync(new TrackingListQuery(Page: -3), CancellationToken.None);

        Assert.Equal(1, page.Page);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task List_FiltersByStatusAndCity()
    {
        await CreateAsync(Tn, "Melbourne");
        await CreateAsync(OtherTn, "Sydney");
        await CreateAsync("APX-10000003", "Melbourne");
        await AdvanceToAsync(Tn, TrackingStatus.PickedUp);

        var pickedUp = await _service.ListAsync(new TrackingListQuery(Status: TrackingStatus.PickedUp), CancellationToken.None);
        var sydney = await _service.ListAsync(new TrackingListQuery(City: "Sydney"), CancellationToken.None);
        var createdInMelbourne = await _service.ListAsync(new TrackingListQuery(Status: TrackingStatus.Created, City: " Melbourne "), CancellationToken.None);

        Assert.Equal(Tn, Assert.Single(pickedUp.Items).TrackingNumber);
        Assert.Equal(OtherTn, Assert.Single(sydney.Items).TrackingNumber);
        Assert.Equal("APX-10000003", Assert.Single(createdInMelbourne.Items).TrackingNumber);
    }

    // ---- scan upload and idempotency ----

    [Fact]
    public async Task Upload_StoresScansAndAdvancesStatusInTimeOrder()
    {
        await CreateAsync();
        var pickup = Guid.NewGuid();
        var arrived = Guid.NewGuid();

        // Sent newest-first on purpose: the service orders by scan time before applying them.
        var result = await _service.UploadScansAsync(Tn, [Scan(arrived, ScanType.Arrived, 10), Scan(pickup, ScanType.PickedUp, 60)], CancellationToken.None);

        Assert.Equal(2, result.Accepted);
        Assert.Equal(0, result.DuplicatesSkipped);
        Assert.Empty(result.Rejected);
        var shipment = await _service.GetAsync(Tn, CancellationToken.None);
        Assert.Equal(TrackingStatus.InTransit, shipment.Status);
        Assert.Equal(arrived, shipment.LatestScan!.ScanId);
        var history = await _service.GetScansAsync(Tn, CancellationToken.None);
        Assert.Equal(new[] { pickup, arrived }, history.Select(s => s.ScanId).ToArray());
    }

    [Fact]
    public async Task Upload_SameBatchTwice_CreatesNoDuplicates()
    {
        await CreateAsync();
        var batch = new[] { Scan(Guid.NewGuid(), ScanType.PickedUp, 60), Scan(Guid.NewGuid(), ScanType.Departed, 30) };

        var first = await _service.UploadScansAsync(Tn, batch, CancellationToken.None);
        var second = await _service.UploadScansAsync(Tn, batch, CancellationToken.None);

        Assert.Equal(2, first.Accepted);
        Assert.Equal(0, second.Accepted);
        Assert.Equal(2, second.DuplicatesSkipped);
        Assert.Empty(second.Rejected);
        Assert.Equal(2, _store.StoredScanCount);
        Assert.Equal(2, (await _service.GetScansAsync(Tn, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Upload_PartlyNewBatch_AcceptsOnlyTheNewScans()
    {
        await CreateAsync();
        var known = Scan(Guid.NewGuid(), ScanType.PickedUp, 60);
        await _service.UploadScansAsync(Tn, [known], CancellationToken.None);

        var result = await _service.UploadScansAsync(Tn, [known, Scan(Guid.NewGuid(), ScanType.Departed, 30)], CancellationToken.None);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(1, result.DuplicatesSkipped);
        Assert.Equal(2, _store.StoredScanCount);
    }

    [Fact]
    public async Task Upload_SameScanIdTwiceInOneRequest_IsCountedOnceAndSkippedOnce()
    {
        await CreateAsync();
        var id = Guid.NewGuid();

        var result = await _service.UploadScansAsync(Tn, [Scan(id, ScanType.PickedUp, 60), Scan(id, ScanType.PickedUp, 60)], CancellationToken.None);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(1, result.DuplicatesSkipped);
        Assert.Equal(1, _store.StoredScanCount);
    }

    [Fact]
    public async Task Upload_ScanIdBelongingToAnotherShipment_IsRejectedWithAReason()
    {
        await CreateAsync();
        await CreateAsync(OtherTn);
        var id = Guid.NewGuid();
        await _service.UploadScansAsync(OtherTn, [Scan(id, ScanType.PickedUp, 60)], CancellationToken.None);

        var result = await _service.UploadScansAsync(Tn, [Scan(id, ScanType.PickedUp, 60)], CancellationToken.None);

        Assert.Equal(0, result.Accepted);
        Assert.Equal(0, result.DuplicatesSkipped);
        var rejected = Assert.Single(result.Rejected);
        Assert.Equal(id, rejected.ScanId);
        Assert.Contains("different shipment", rejected.Reason);
    }

    [Fact]
    public async Task Upload_DeliveredScanWithoutSignature_IsRejectedButTheRestOfTheBatchIsStored()
    {
        await CreateAsync();
        var bad = Guid.NewGuid();
        var good = Guid.NewGuid();

        var result = await _service.UploadScansAsync(Tn, [Scan(bad, ScanType.Delivered, 10), Scan(good, ScanType.PickedUp, 60)], CancellationToken.None);

        Assert.Equal(1, result.Accepted);
        var rejected = Assert.Single(result.Rejected);
        Assert.Equal(bad, rejected.ScanId);
        Assert.Contains("signature", rejected.Reason);
        Assert.Equal(1, _store.StoredScanCount);
    }

    [Fact]
    public async Task Upload_ToAFinishedShipment_IsRejected()
    {
        await CreateAsync();
        await AdvanceToAsync(Tn, TrackingStatus.PickedUp, TrackingStatus.InTransit, TrackingStatus.OutForDelivery, TrackingStatus.Delivered);

        var result = await _service.UploadScansAsync(Tn, [Scan(Guid.NewGuid(), ScanType.Arrived, 5)], CancellationToken.None);

        Assert.Equal(0, result.Accepted);
        Assert.Contains("Delivered", Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public async Task Upload_ScanThatWouldSkipAStatus_IsStoredButDoesNotMoveTheShipmentBackwardsOrSideways()
    {
        await CreateAsync();

        var result = await _service.UploadScansAsync(Tn, [Scan(Guid.NewGuid(), ScanType.Arrived, 10)], CancellationToken.None);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(TrackingStatus.Created, (await _service.GetAsync(Tn, CancellationToken.None)).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Upload_EmptyOrOversizedBatch_IsAValidationError(int count)
    {
        await CreateAsync();
        var batch = Enumerable.Range(0, count).Select(i => Scan(Guid.NewGuid(), ScanType.Arrived, i + 1)).ToList();

        await Assert.ThrowsAsync<CommandValidationException>(() => _service.UploadScansAsync(Tn, batch, CancellationToken.None));
    }

    [Fact]
    public async Task Upload_WhenAnotherRequestStoresAScanFirst_TheLoserIsCountedAsADuplicate()
    {
        await CreateAsync();
        var raced = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        // After our duplicate check and before our commit, a concurrent upload stores `raced`.
        _store.BeforeNextCommit = () => _store.InjectScan(Tn, raced);

        var result = await _service.UploadScansAsync(Tn, [Scan(raced, ScanType.PickedUp, 60), Scan(fresh, ScanType.Departed, 30)], CancellationToken.None);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(1, result.DuplicatesSkipped);
        Assert.Empty(result.Rejected);
        Assert.Equal(2, _store.StoredScanCount);
        var history = await _service.GetScansAsync(Tn, CancellationToken.None);
        Assert.Equal(2, history.Count);
        Assert.Contains(history, s => s.ScanId == fresh);
    }
}
