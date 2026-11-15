using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.UnitTests.Support;

namespace ApexPost.UnitTests.Domain;

/// <summary>
/// Risk R1 (TESTING-STRATEGY.md): the shipment status machine. Pure domain logic, so it is tested at the cheapest level
/// with the complete 6 x 6 transition matrix, not a hand-picked sample.
/// </summary>
public sealed class TrackedShipmentStatusTests
{
    // The only legal moves. Everything else in the 36-pair matrix must be refused.
    private static readonly HashSet<(TrackingStatus From, TrackingStatus To)> Allowed =
    [
        (TrackingStatus.Created, TrackingStatus.PickedUp),
        (TrackingStatus.PickedUp, TrackingStatus.InTransit),
        (TrackingStatus.InTransit, TrackingStatus.OutForDelivery),
        (TrackingStatus.OutForDelivery, TrackingStatus.Delivered),
        (TrackingStatus.OutForDelivery, TrackingStatus.Failed),
    ];

    public static TheoryData<TrackingStatus, TrackingStatus, bool> EveryTransition()
    {
        var data = new TheoryData<TrackingStatus, TrackingStatus, bool>();
        foreach (var from in Enum.GetValues<TrackingStatus>())
        {
            foreach (var to in Enum.GetValues<TrackingStatus>())
            {
                data.Add(from, to, Allowed.Contains((from, to)));
            }
        }

        return data;
    }

    public static TheoryData<TrackingStatus, TrackingStatus> IllegalTransitions()
    {
        var data = new TheoryData<TrackingStatus, TrackingStatus>();
        foreach (var row in EveryTransition())
        {
            if (!(bool)row[2])
            {
                data.Add((TrackingStatus)row[0], (TrackingStatus)row[1]);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryTransition))]
    public void CanMoveTo_EveryStatusPair_MatchesTheTransitionTable(TrackingStatus from, TrackingStatus to, bool expected)
    {
        // Arrange (the pair comes from the theory data)

        // Act
        var allowed = TrackedShipment.CanMoveTo(from, to);

        // Assert
        Assert.Equal(expected, allowed);
    }

    [Theory]
    [MemberData(nameof(IllegalTransitions))]
    public void ChangeStatus_IllegalTransition_ThrowsConflictAndKeepsStatus(TrackingStatus from, TrackingStatus to)
    {
        // Arrange
        var shipment = TrackingTestData.Shipment(from);

        // Act
        var ex = Assert.Throws<ConflictException>(() => shipment.ChangeStatus(to));

        // Assert
        Assert.Equal("INVALID_STATUS_TRANSITION", ex.ErrorCode);
        Assert.Equal(from, shipment.Status);
    }

    [Fact]
    public void ChangeStatus_DeliveredBackToInTransit_ThrowsConflict()
    {
        // Arrange
        var shipment = TrackingTestData.Shipment(TrackingStatus.Delivered);

        // Act
        var ex = Assert.Throws<ConflictException>(() => shipment.ChangeStatus(TrackingStatus.InTransit));

        // Assert
        Assert.Equal("INVALID_STATUS_TRANSITION", ex.ErrorCode);
        Assert.Contains("Delivered", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeStatus_UndefinedEnumValue_ThrowsArgumentException()
    {
        // Arrange
        var shipment = TrackingTestData.Shipment();

        // Act + Assert (the guard rejects the value before the transition table is consulted)
        Assert.ThrowsAny<ArgumentException>(() => shipment.ChangeStatus((TrackingStatus)99));
    }

    [Theory]
    [InlineData(TrackingStatus.Delivered, ScanType.Arrived)]
    [InlineData(TrackingStatus.Delivered, ScanType.PickedUp)]
    [InlineData(TrackingStatus.OutForDelivery, ScanType.Departed)]
    [InlineData(TrackingStatus.Failed, ScanType.Delivered)]
    public void AdvanceFor_ScanThatWouldMoveBackwards_ReturnsFalseAndKeepsStatus(TrackingStatus current, ScanType scan)
    {
        // Arrange
        var shipment = TrackingTestData.Shipment(current);

        // Act
        var changed = shipment.AdvanceFor(scan);

        // Assert
        Assert.False(changed);
        Assert.Equal(current, shipment.Status);
    }

    [Theory]
    [InlineData(TrackingStatus.Created, ScanType.PickedUp, TrackingStatus.PickedUp)]
    [InlineData(TrackingStatus.PickedUp, ScanType.Arrived, TrackingStatus.InTransit)]
    [InlineData(TrackingStatus.PickedUp, ScanType.Departed, TrackingStatus.InTransit)]
    [InlineData(TrackingStatus.OutForDelivery, ScanType.DeliveryFailed, TrackingStatus.Failed)]
    public void AdvanceFor_NextLegalScan_MovesForward(TrackingStatus current, ScanType scan, TrackingStatus expected)
    {
        // Arrange
        var shipment = TrackingTestData.Shipment(current);

        // Act
        var changed = shipment.AdvanceFor(scan);

        // Assert
        Assert.True(changed);
        Assert.Equal(expected, shipment.Status);
    }

    [Theory]
    [InlineData(TrackingStatus.PickedUp)]
    [InlineData(TrackingStatus.Delivered)]
    public void EnsureDeletable_PastCreated_ThrowsConflict(TrackingStatus status)
    {
        // Arrange
        var shipment = TrackingTestData.Shipment(status);

        // Act
        var ex = Assert.Throws<ConflictException>(shipment.EnsureDeletable);

        // Assert
        Assert.Equal("SHIPMENT_NOT_DELETABLE", ex.ErrorCode);
    }
}
