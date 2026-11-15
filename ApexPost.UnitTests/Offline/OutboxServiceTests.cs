using ApexPost.Courier.Offline.Application;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.UnitTests.Offline;

/// <summary>
/// Risk R6 (TESTING-STRATEGY.md): the outbox must never lose a scan. Tested as interactions with the local store and
/// the uploader: which scans are marked Synced/Failed, and that nothing is touched when the server cannot be reached.
///
/// What these mocks do NOT prove: that the Couchbase Lite store really persists the new status or returns Pending
/// scans oldest first. That is proven by ApexPost.Courier.Offline.Tests on a real database.
/// </summary>
public sealed class OutboxServiceTests
{
    private static readonly DateTimeOffset ScannedAt = new(2026, 1, 15, 7, 30, 0, TimeSpan.Zero);

    private readonly Mock<IScanRepository> _scans = new();
    private readonly Mock<IShipmentUploader> _uploader = new();

    private static ShipmentScan Pending(int minute = 0) =>
        ShipmentScan.CreateNew("APX-10000001", ScanType.InTransit, "C-1001", "Melbourne Depot", ScannedAt.AddMinutes(minute));

    [Fact]
    public async Task SyncPendingAsync_AcceptedAndRejectedScans_MarksEachSyncedOrFailed()
    {
        // Arrange
        var accepted = Pending(0);
        var rejected = Pending(1);
        _scans.SetupSequence(s => s.GetPending(It.IsAny<int?>()))
            .Returns([accepted, rejected])
            .Returns([]);
        _uploader.Setup(u => u.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ScanUploadOutcome(accepted.ScanId, true), new ScanUploadOutcome(rejected.ScanId, false, "Unknown shipment")]);
        var outbox = new OutboxService(_scans.Object, _uploader.Object);

        // Act
        var result = await outbox.SyncPendingAsync();

        // Assert
        Assert.Equal(new OutboxSyncResult(2, 1, 1, false, null), result);
        _scans.Verify(s => s.Update(It.Is<ShipmentScan>(x => x.ScanId == accepted.ScanId && x.SyncStatus == SyncStatus.Synced)), Times.Once);
        _scans.Verify(s => s.Update(It.Is<ShipmentScan>(x => x.ScanId == rejected.ScanId && x.SyncStatus == SyncStatus.Failed)), Times.Once);
    }

    [Fact]
    public async Task SyncPendingAsync_ServerUnreachable_LeavesEveryScanPendingAndReportsWhy()
    {
        // Arrange
        _scans.Setup(s => s.GetPending(It.IsAny<int?>())).Returns([Pending(0), Pending(1)]);
        _uploader.Setup(u => u.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UploaderUnavailableException("No connection."));
        var outbox = new OutboxService(_scans.Object, _uploader.Object);

        // Act
        var result = await outbox.SyncPendingAsync();

        // Assert
        Assert.True(result.Interrupted);
        Assert.Equal("No connection.", result.Error);
        Assert.Equal(0, result.Attempted);
        _scans.Verify(s => s.Update(It.IsAny<ShipmentScan>()), Times.Never);
    }

    [Fact]
    public async Task SyncPendingAsync_MoreScansThanOneBatch_ReadsBatchesOfTheConfiguredSizeUntilEmpty()
    {
        // Arrange
        ShipmentScan[] first = [Pending(0), Pending(1)];
        ShipmentScan[] second = [Pending(2)];
        _scans.SetupSequence(s => s.GetPending(It.IsAny<int?>()))
            .Returns(first)
            .Returns(second)
            .Returns([]);
        _uploader.Setup(u => u.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ShipmentScan> batch, CancellationToken _) => batch.Select(s => new ScanUploadOutcome(s.ScanId, true)).ToList());
        var outbox = new OutboxService(_scans.Object, _uploader.Object, batchSize: 2);

        // Act
        var result = await outbox.SyncPendingAsync();

        // Assert
        Assert.Equal(3, result.Synced);
        _scans.Verify(s => s.GetPending(It.Is<int?>(take => take == 2)), Times.Exactly(3));
        _uploader.Verify(u => u.UploadAsync(It.Is<IReadOnlyList<ShipmentScan>>(b => b.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
        _uploader.Verify(u => u.UploadAsync(It.Is<IReadOnlyList<ShipmentScan>>(b => b.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncPendingAsync_NothingPending_NeverCallsTheUploader()
    {
        // Arrange
        _scans.Setup(s => s.GetPending(It.IsAny<int?>())).Returns([]);
        var outbox = new OutboxService(_scans.Object, _uploader.Object);

        // Act
        var result = await outbox.SyncPendingAsync();

        // Assert
        Assert.Equal(new OutboxSyncResult(0, 0, 0, false, null), result);
        _uploader.Verify(u => u.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncPendingAsync_CallerCancelsDuringUpload_PropagatesCancellationAndUpdatesNothing()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _scans.Setup(s => s.GetPending(It.IsAny<int?>())).Returns([Pending()]);
        _uploader.Setup(u => u.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<ShipmentScan> _, CancellationToken token) =>
            {
                await cts.CancelAsync();
                token.ThrowIfCancellationRequested();
                return [];
            });
        var outbox = new OutboxService(_scans.Object, _uploader.Object);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => outbox.SyncPendingAsync(cts.Token));

        // Assert: cancellation is not a failure; the scan stays Pending
        _scans.Verify(s => s.Update(It.IsAny<ShipmentScan>()), Times.Never);
    }

    [Fact]
    public async Task SyncPendingAsync_UploaderAnswersForNoScan_StopsInsteadOfLoopingForever()
    {
        // Arrange: the same Pending scan would be returned forever if the outbox kept going
        _scans.Setup(s => s.GetPending(It.IsAny<int?>())).Returns([Pending()]);
        _uploader.Setup(u => u.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var outbox = new OutboxService(_scans.Object, _uploader.Object);

        // Act
        var result = await outbox.SyncPendingAsync();

        // Assert
        Assert.True(result.Interrupted);
        _uploader.Verify(u => u.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_BatchSizeBelowOne_ThrowsArgumentOutOfRange(int batchSize)
    {
        // Act + Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboxService(_scans.Object, _uploader.Object, batchSize));
    }

    [Fact]
    public void Constructor_NullRepository_ThrowsArgumentNull()
    {
        // Act + Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new OutboxService(null!, _uploader.Object));
        Assert.Equal("scans", ex.ParamName);
    }
}
