// Copilot-assisted: drafted by GitHub Copilot (prompt 1 in COPILOT-LOG.md), reviewed and edited by the author.
// Each test says whether it was kept, changed, or added during the review. Copilot's raw output is kept in
// docs/copilot/raw/prompt1-SyncCoordinatorTests.cs.txt for comparison.

using ApexPost.Courier.Core.Resilience;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Offline.Application;
using ApexPost.Courier.Offline.Domain;
using Microsoft.Extensions.Time.Testing;

namespace ApexPost.UnitTests.Sync;

/// <summary>
/// SyncCoordinator decides WHEN the outbox runs (online only, one at a time) and what the Sync Status page shows.
/// The real (sealed) OutboxService is used; only its ports, the network status and the circuit monitor are mocked.
/// What these mocks do NOT prove: real connectivity events from MAUI, or real Polly circuit transitions
/// (those are covered by ApexPost.Courier.Core.Tests with the real pipeline).
/// </summary>
public sealed class SyncCoordinatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 2, 9, 15, 0, TimeSpan.Zero);

    private readonly List<ShipmentScan> _stored = [];
    private readonly Mock<IScanRepository> _scans = new();
    private readonly Mock<IShipmentUploader> _uploader = new();
    private readonly Mock<INetworkStatus> _network = new();
    private readonly Mock<ICircuitStateMonitor> _circuit = new();
    private readonly FakeTimeProvider _clock = new(Start);

    public SyncCoordinatorTests()
    {
        // Review change: the phone store is driven by STATE (which scans are still Pending), not by a queue of
        // canned CountPending answers. Copilot's Queue([1, 0]) encoded how many times the code calls CountPending,
        // which couples the test to the implementation and breaks on a harmless refactor.
        _scans.Setup(s => s.GetPending(It.IsAny<int?>()))
            .Returns((int? take) => [.. _stored.Where(x => x.SyncStatus == SyncStatus.Pending).Take(take ?? int.MaxValue)]);
        _scans.Setup(s => s.CountPending()).Returns(() => _stored.Count(x => x.SyncStatus == SyncStatus.Pending));
        _scans.Setup(s => s.Update(It.IsAny<ShipmentScan>())).Returns((ShipmentScan scan) =>
        {
            var index = _stored.FindIndex(x => x.ScanId == scan.ScanId);
            _stored[index] = scan;
            return true;
        });
        _network.SetupGet(n => n.IsOnline).Returns(true);
        _circuit.SetupGet(c => c.State).Returns(CircuitStateKind.Closed);
    }

    private ShipmentScan AddPendingScan(int minutesAgo = 5)
    {
        var scan = ShipmentScan.CreateNew("APX-20000001", ScanType.Pickup, "C-1001", "Melbourne Depot", _clock.GetUtcNow().AddMinutes(-minutesAgo));
        _stored.Add(scan);
        return scan;
    }

    private void ServerAcceptsEverything() =>
        _uploader.Setup(u => u.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ShipmentScan> batch, CancellationToken _) => [.. batch.Select(s => new ScanUploadOutcome(s.ScanId, true))]);

    private SyncCoordinator CreateCoordinator() =>
        new(new OutboxService(_scans.Object, _uploader.Object), _scans.Object, _network.Object, _circuit.Object, _clock);

    // Copilot test, CHANGED: CountPending queue replaced by state; added "the local store is never read".
    [Fact]
    public async Task SyncNowAsync_Offline_ReturnsNullAndNeverTouchesTheOutbox()
    {
        // Arrange
        AddPendingScan();
        AddPendingScan();
        _network.SetupGet(n => n.IsOnline).Returns(false);
        using var coordinator = CreateCoordinator();

        // Act
        var result = await coordinator.SyncNowAsync();

        // Assert
        Assert.Null(result);
        Assert.False(coordinator.Current.IsOnline);
        Assert.Equal(2, coordinator.Current.PendingCount);
        Assert.Equal("Offline: scans are saved on this phone.", coordinator.Current.LastMessage);
        _scans.Verify(s => s.GetPending(It.IsAny<int?>()), Times.Never);
        _uploader.Verify(u => u.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Copilot test, CHANGED: state-driven pending count; realistic tracking number.
    [Fact]
    public async Task SyncNowAsync_PendingScansAndServerAccepts_UploadsThemAndRecordsSuccess()
    {
        // Arrange
        var scan = AddPendingScan();
        ServerAcceptsEverything();
        using var coordinator = CreateCoordinator();

        // Act
        var result = await coordinator.SyncNowAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Attempted);
        Assert.Equal(1, result.Synced);
        Assert.Equal(0, result.Failed);
        Assert.False(result.Interrupted);
        Assert.Equal(0, coordinator.Current.PendingCount);
        Assert.Equal(Start, coordinator.Current.LastAttemptUtc);
        Assert.Equal(Start, coordinator.Current.LastSuccessUtc);
        Assert.Equal("Uploaded 1, rejected 0.", coordinator.Current.LastMessage);
        _uploader.Verify(u => u.UploadAsync(
            It.Is<IReadOnlyList<ShipmentScan>>(batch => batch.Count == 1 && batch[0].ScanId == scan.ScanId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Copilot test, KEPT (only the shared fixture changed). Used for mutation proof 1 in COPILOT-LOG.md.
    [Fact]
    public async Task SyncNowAsync_ServerUnavailable_InterruptsAndKeepsScansPending()
    {
        // Arrange
        AddPendingScan();
        _uploader.Setup(u => u.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UploaderUnavailableException("server timeout"));
        using var coordinator = CreateCoordinator();

        // Act
        var result = await coordinator.SyncNowAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Interrupted);
        Assert.Equal("server timeout", result.Error);
        Assert.Equal(1, coordinator.Current.PendingCount);
        Assert.Equal(Start, coordinator.Current.LastAttemptUtc);
        Assert.Null(coordinator.Current.LastSuccessUtc);
        Assert.Equal("Server unavailable: server timeout. Scans stay on this phone.", coordinator.Current.LastMessage);
    }

    // ADDED in review (human-written): the edge case Copilot's frozen clock could not express.
    [Fact]
    public async Task SyncNowAsync_ServerUnavailableAfterEarlierSuccess_KeepsLastSuccessAndMovesLastAttempt()
    {
        // Arrange: one successful sync at Start ...
        AddPendingScan();
        ServerAcceptsEverything();
        using var coordinator = CreateCoordinator();
        await coordinator.SyncNowAsync();

        // ... then, ten minutes later, a new scan and a server that cannot be reached
        _clock.Advance(TimeSpan.FromMinutes(10));
        AddPendingScan(minutesAgo: 1);
        _uploader.Setup(u => u.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UploaderUnavailableException("server timeout"));

        // Act
        await coordinator.SyncNowAsync();

        // Assert: the page must say "last attempt just now, last success 10 minutes ago", not pretend it succeeded
        Assert.Equal(Start.AddMinutes(10), coordinator.Current.LastAttemptUtc);
        Assert.Equal(Start, coordinator.Current.LastSuccessUtc);
        Assert.Equal(1, coordinator.Current.PendingCount);
    }

    // Copilot test, CHANGED: removed the tautology Assert.Equal(2, results.Length) (Task.WhenAll of two tasks
    // always returns two results, so that line could never fail).
    [Fact]
    public async Task SyncNowAsync_SecondSyncWhileFirstIsRunning_ReturnsNullAndUploadsOnce()
    {
        // Arrange
        var scan = AddPendingScan();
        var uploadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowUploadToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _uploader.Setup(u => u.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                uploadStarted.SetResult();
                await allowUploadToFinish.Task;
                return [new ScanUploadOutcome(scan.ScanId, true)];
            });
        using var coordinator = CreateCoordinator();

        // Act
        var first = coordinator.SyncNowAsync();
        await uploadStarted.Task;
        var second = await coordinator.SyncNowAsync();   // while the first one is still uploading
        allowUploadToFinish.SetResult();
        var firstResult = await first;

        // Assert
        Assert.Null(second);
        Assert.NotNull(firstResult);
        Assert.Equal(1, firstResult.Synced);
        _uploader.Verify(u => u.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // REPLACES Copilot's SyncCoordinator_ChangedEvent_UpdatesSnapshotValues (REJECTED: name did not follow the
    // convention, Assert.NotEmpty was weak, and it only re-checked the final state already covered above, so it
    // never tested the event sequence its name promised). Human-written.
    [Fact]
    public async Task SyncNowAsync_WhileRunning_PublishesSyncingThenIdleSnapshots()
    {
        // Arrange
        AddPendingScan();
        ServerAcceptsEverything();
        using var coordinator = CreateCoordinator();
        var published = new List<SyncSnapshot>();
        coordinator.Changed += (_, snapshot) => published.Add(snapshot);

        // Act
        await coordinator.SyncNowAsync();

        // Assert: the UI first shows a spinner with the scan still pending, then the result
        Assert.Equal(2, published.Count);
        Assert.True(published[0].IsSyncing);
        Assert.Equal(1, published[0].PendingCount);
        Assert.False(published[1].IsSyncing);
        Assert.Equal(0, published[1].PendingCount);
        Assert.Equal(coordinator.Current, published[1]);
    }

    // ADDED in review (human-written): a missing edge case. Cancelling must not leave the spinner on forever.
    [Fact]
    public async Task SyncNowAsync_CallerCancelsDuringUpload_RethrowsAndStopsShowingSyncing()
    {
        // Arrange
        AddPendingScan();
        using var cts = new CancellationTokenSource();
        _uploader.Setup(u => u.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<ShipmentScan> _, CancellationToken token) =>
            {
                await cts.CancelAsync();
                token.ThrowIfCancellationRequested();
                return [];
            });
        using var coordinator = CreateCoordinator();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.SyncNowAsync(cts.Token));

        // Assert
        Assert.False(coordinator.Current.IsSyncing);
        Assert.Equal(1, coordinator.Current.PendingCount);
    }

    // ADDED in review (human-written): the circuit state is part of the snapshot the page shows.
    [Fact]
    public void CircuitStateChanged_Opened_IsPublishedInTheSnapshot()
    {
        // Arrange
        using var coordinator = CreateCoordinator();
        SyncSnapshot? published = null;
        coordinator.Changed += (_, snapshot) => published = snapshot;

        // Act
        _circuit.Raise(c => c.StateChanged += null, _circuit.Object, CircuitStateKind.Open);

        // Assert
        Assert.NotNull(published);
        Assert.Equal(CircuitStateKind.Open, published.Circuit);
        Assert.Equal(CircuitStateKind.Open, coordinator.Current.Circuit);
    }
}
