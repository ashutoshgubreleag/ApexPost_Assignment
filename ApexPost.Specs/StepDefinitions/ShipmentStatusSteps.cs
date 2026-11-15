// Copilot-assisted: drafted by GitHub Copilot (prompt 2 in COPILOT-LOG.md), reviewed and edited by the author.
// Raw Copilot output: docs/copilot/raw/prompt2-ShipmentStatusSteps.cs.txt

using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Services.Tracking;
using ApexPost.Specs.Support;

namespace ApexPost.Specs.StepDefinitions;

/// <summary>Bindings for ShipmentStatus.feature. Shares scenario state through the injected <see cref="ShipmentWorld"/> (kept from Copilot).</summary>
[Binding]
public sealed class ShipmentStatusSteps(ShipmentWorld world)
{
    // Review change (REJECTED Copilot's AdvanceToStatusAsync): Copilot walked one list
    // [Created ... Delivered, Failed], so reaching "failed" went THROUGH Delivered, and Delivered -> Failed is illegal.
    // It also re-implemented the production status order inside the test. These explicit paths are the business
    // journeys, written out so a reader can check them without knowing the implementation.
    private static readonly Dictionary<TrackingStatus, TrackingStatus[]> PathFromCreated = new()
    {
        [TrackingStatus.Created] = [],
        [TrackingStatus.PickedUp] = [TrackingStatus.PickedUp],
        [TrackingStatus.InTransit] = [TrackingStatus.PickedUp, TrackingStatus.InTransit],
        [TrackingStatus.OutForDelivery] = [TrackingStatus.PickedUp, TrackingStatus.InTransit, TrackingStatus.OutForDelivery],
        [TrackingStatus.Delivered] = [TrackingStatus.PickedUp, TrackingStatus.InTransit, TrackingStatus.OutForDelivery, TrackingStatus.Delivered],
        [TrackingStatus.Failed] = [TrackingStatus.PickedUp, TrackingStatus.InTransit, TrackingStatus.OutForDelivery, TrackingStatus.Failed],
    };

    private ScanUploadResult? _lateScanResult;

    /// <summary>Review change: the feature now uses business words ("out for delivery"), mapped to the enum here.</summary>
    private static TrackingStatus Parse(string words) =>
        Enum.TryParse<TrackingStatus>(words.Replace(" ", string.Empty, StringComparison.Ordinal), ignoreCase: true, out var status)
            ? status
            : throw new InvalidOperationException($"Unknown shipment status '{words}'.");

    [Given(@"^a shipment ""(APX-\d+)"" exists for ""(.*)"" in ""(.*)""$")]
    public async Task GivenAShipmentExists(string trackingNumber, string recipientName, string city)
    {
        await world.Tracking.CreateAsync(
            new CreateTrackedShipmentCommand(trackingNumber, new ShipmentDetails(recipientName, "+61 400 000 111", "1 Test Street", city, 2.5m)),
            CancellationToken.None);
        world.CurrentParcel = trackingNumber;
    }

    [Given(@"^the shipment has already reached ""(.*)""$")]
    public async Task GivenTheShipmentHasAlreadyReached(string status)
    {
        foreach (var step in PathFromCreated[Parse(status)])
        {
            await world.Tracking.ChangeStatusAsync(world.CurrentParcel, step, CancellationToken.None);
        }
    }

    [When(@"^the shipment is moved to ""(.*)""$")]
    public Task WhenTheShipmentIsMovedTo(string status) =>
        world.TryAsync(() => world.Tracking.ChangeStatusAsync(world.CurrentParcel, Parse(status), CancellationToken.None));

    [When(@"^a late ""(.*)"" scan from ""(.*)"" is uploaded for the shipment$")]
    public async Task WhenALateScanIsUploaded(string scanType, string location) =>
        _lateScanResult = await world.Tracking.UploadScansAsync(
            world.CurrentParcel,
            [new ScanInput(Guid.NewGuid(), Enum.Parse<ScanType>(scanType), location, world.Clock.GetUtcNow().AddHours(-3), null)],
            CancellationToken.None);

    [Then(@"^the shipment status is ""(.*)""$")]
    public async Task ThenTheShipmentStatusIs(string status) =>
        Assert.Equal(Parse(status), (await world.Tracking.GetAsync(world.CurrentParcel, CancellationToken.None)).Status);

    // Review change: Copilot had two Then steps ("cannot move backwards" / "must progress one step at a time") with
    // IDENTICAL assertions; the wording promised a difference nothing checked. Merged into one honest step.
    [Then("the status change is refused")]
    public void ThenTheStatusChangeIsRefused()
    {
        var conflict = Assert.IsType<ConflictException>(world.LastError);
        Assert.Equal("INVALID_STATUS_TRANSITION", conflict.ErrorCode);
    }

    [Then("the late scan is not accepted")]
    public void ThenTheLateScanIsNotAccepted()
    {
        Assert.NotNull(_lateScanResult);
        Assert.Equal(0, _lateScanResult.Accepted);
        Assert.Contains("already Delivered", Assert.Single(_lateScanResult.Rejected).Reason, StringComparison.Ordinal);
    }

    // Review change: removed Copilot's unused binding 'the shipment is currently "..."' (no scenario used it).
}
