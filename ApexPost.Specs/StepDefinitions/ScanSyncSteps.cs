using ApexPost.Courier.Offline.Domain;
using ApexPost.Services.Tracking;
using ApexPost.Specs.Support;
using TrackingStatus = ApexPost.Models.Domain.TrackingStatus;

namespace ApexPost.Specs.StepDefinitions;

/// <summary>
/// Bindings for ScanSync.feature. The real courier OutboxService on the "phone" talks to the real server-side
/// ShipmentTrackingService through an in-process uploader: the whole offline-to-server journey, without a network.
/// </summary>
[Binding]
public sealed class ScanSyncSteps(ShipmentWorld world)
{
    private DateTimeOffset AtEight(int minutes) =>
        new DateTimeOffset(world.Clock.GetUtcNow().Date, TimeSpan.Zero).AddHours(8).AddMinutes(minutes);

    private ShipmentScan Record(string name, ScanType type, string location, int minutesAfterEight)
    {
        var scan = world.Phone.Create(ShipmentScan.CreateNew(world.CurrentParcel, type, world.CourierId, location, AtEight(minutesAfterEight)));
        world.NamedScans[name] = scan;
        return scan;
    }

    [Given(@"^parcel ""(APX-\d+)"" is registered for delivery$")]
    public async Task GivenParcelIsRegisteredForDelivery(string tn)
    {
        await world.Tracking.CreateAsync(
            new CreateTrackedShipmentCommand(tn, new ShipmentDetails("Test Recipient", "+61 400 000 111", "12 Example Street", "Melbourne", 2.5m)),
            CancellationToken.None);
        world.CurrentParcel = tn;
    }

    [Given(@"^courier ""(.*)"" records scans on their phone$")]
    public void GivenCourierRecordsScansOnTheirPhone(string courierId)
    {
        world.CourierId = courierId;
        Assert.Equal(0, world.Phone.CountPending()); // a fresh phone: nothing waiting yet
    }

    [Given("the courier records these scans while offline:")]
    public void GivenTheCourierRecordsTheseScans(DataTable table)
    {
        foreach (var row in table.Rows)
        {
            Record(row["scan"], Enum.Parse<ScanType>(row["type"]), row["location"], int.Parse(row["minutes after 8am"], System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Given(@"^the courier recorded a ""(.*)"" scan called ""(.*)"" at ""(.*)""$")]
    public void GivenTheCourierRecordedAScan(string type, string name, string location) =>
        Record(name, Enum.Parse<ScanType>(type), location, 0);

    [Given(@"^the courier recorded (\d+) pickup scans$")]
    public void GivenTheCourierRecordedNPickupScans(int count)
    {
        for (var i = 0; i < count; i++)
        {
            Record($"pickup-{i}", ScanType.Pickup, "Melbourne Depot", i);
        }
    }

    [Given(@"^the scan ""(.*)"" reached the tracking system but the phone never heard back$")]
    public async Task GivenTheScanReachedTheServerButTheAnswerWasLost(string name)
    {
        // The server stored the scan; the phone still has it as Pending (the response was lost on a bad network).
        var result = await world.Tracking.UploadScansAsync(world.CurrentParcel, [InProcessUploader.ToServer(world.NamedScans[name])], CancellationToken.None);
        Assert.Equal(1, result.Accepted);
        Assert.Equal(SyncStatus.Pending, world.Phone.Get(world.NamedScans[name].ScanId)!.SyncStatus);
    }

    [Given("the tracking system cannot be reached")]
    public void GivenTheTrackingSystemCannotBeReached() => world.Uploader.ServerReachable = false;

    [When("the phone syncs with the tracking system")]
    public async Task WhenThePhoneSyncs() => world.LastSync = await world.Outbox.SyncPendingAsync(CancellationToken.None);

    [Then(@"^(\d+) scans are confirmed by the tracking system$")]
    public void ThenNScansAreConfirmed(int expected)
    {
        Assert.NotNull(world.LastSync);
        Assert.Equal(expected, world.LastSync.Synced);
        Assert.Equal(0, world.LastSync.Failed);
    }

    [Then("no scans are waiting on the phone")]
    public void ThenNoScansAreWaiting() => Assert.Equal(0, world.Phone.CountPending());

    [Then(@"^(\d+) scans are still waiting on the phone$")]
    public void ThenNScansAreStillWaiting(int expected) => Assert.Equal(expected, world.Phone.CountPending());

    [Then(@"^parcel ""(APX-\d+)"" shows that it is in transit$")]
    public async Task ThenParcelIsInTransit(string tn) =>
        Assert.Equal(TrackingStatus.InTransit, (await world.Tracking.GetAsync(tn, CancellationToken.None)).Status);

    [Then(@"^the scan ""(.*)"" is confirmed on the phone$")]
    public void ThenTheScanIsConfirmedOnThePhone(string name) =>
        Assert.Equal(SyncStatus.Synced, world.Phone.Get(world.NamedScans[name].ScanId)!.SyncStatus);

    [Then(@"^the scan ""(.*)"" is marked as rejected on the phone because a signature is required$")]
    public void ThenTheScanIsRejectedForMissingSignature(string name)
    {
        var scanId = world.NamedScans[name].ScanId;
        Assert.Equal(SyncStatus.Failed, world.Phone.Get(scanId)!.SyncStatus);
        Assert.Contains("signature", world.Uploader.Outcomes[scanId].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Then(@"^the tracking system holds (\d+) scans? for parcel ""(APX-\d+)""$")]
    public async Task ThenTheTrackingSystemHoldsNScans(int expected, string tn) =>
        Assert.Equal(expected, (await world.Tracking.GetScansAsync(tn, CancellationToken.None)).Count);

    [Then("the phone reports that the tracking system is unavailable")]
    public void ThenThePhoneReportsUnavailable()
    {
        Assert.NotNull(world.LastSync);
        Assert.True(world.LastSync.Interrupted);
        Assert.Contains("No connection", world.LastSync.Error, StringComparison.Ordinal);
        Assert.Equal(0, world.LastSync.Synced);
    }
}
