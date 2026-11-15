using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Services.Tracking;
using ApexPost.Specs.Support;

namespace ApexPost.Specs.StepDefinitions;

/// <summary>Bindings for ShipmentCreation.feature. Runs the real ShipmentTrackingService (service level, no HTTP).</summary>
[Binding]
public sealed class ShipmentCreationSteps(ShipmentWorld world)
{
    private static CreateTrackedShipmentCommand Command(string tn, string recipient = "Test Recipient", string city = "Melbourne", decimal kg = 2.5m) =>
        new(tn, new ShipmentDetails(recipient, "+61 400 000 111", "1 Test Street", city, kg));

    [Given("the tracking service is open for bookings")]
    public void GivenTheTrackingServiceIsOpen() =>
        Assert.NotNull(world.Tracking); // the BeforeScenario hook built a fresh, empty service

    [Given(@"^parcel ""(APX-\d+)"" has already been registered$")]
    public Task GivenParcelAlreadyRegistered(string tn) =>
        world.Tracking.CreateAsync(Command(tn), CancellationToken.None);

    [Given(@"^parcel ""(APX-\d+)"" weighing ([\d.]+) kg has been registered$")]
    public Task GivenParcelWeighingHasBeenRegistered(string tn, decimal kg) =>
        world.Tracking.CreateAsync(Command(tn, kg: kg), CancellationToken.None);

    [When(@"^the dispatcher registers parcel ""(.*)"" for ""(.*)"" in ""(.*)"" weighing (-?[\d.]+) kg$")]
    public Task WhenTheDispatcherRegistersParcel(string tn, string recipient, string city, decimal kg) =>
        world.TryAsync(() => world.Tracking.CreateAsync(Command(tn, recipient, city, kg), CancellationToken.None));

    [When(@"^the dispatcher checks the charges for parcel ""(APX-\d+)""$")]
    public async Task WhenTheDispatcherChecksTheCharges(string tn) =>
        world.LastCharges = await world.Tracking.GetChargesAsync(tn, CancellationToken.None);

    [Then(@"^parcel ""(APX-\d+)"" is registered and waiting for pickup$")]
    public async Task ThenParcelIsRegisteredAndWaiting(string tn)
    {
        Assert.Null(world.LastError);
        var parcel = await world.Tracking.GetAsync(tn, CancellationToken.None);
        Assert.Equal(TrackingStatus.Created, parcel.Status);
        Assert.Equal(world.Clock.GetUtcNow(), parcel.CreatedAt);
    }

    [Then(@"^parcel ""(APX-\d+)"" appears when searching for parcels in ""(.*)""$")]
    public async Task ThenParcelAppearsInCitySearch(string tn, string city)
    {
        var page = await world.Tracking.ListAsync(new TrackingListQuery(City: city), CancellationToken.None);
        Assert.Contains(page.Items, p => p.TrackingNumber == tn);
    }

    [Then("the registration is refused because the tracking number is already in use")]
    public void ThenRefusedAsDuplicate()
    {
        var conflict = Assert.IsType<ConflictException>(world.LastError);
        Assert.Equal("DUPLICATE_TRACKING_NUMBER", conflict.ErrorCode);
    }

    [Then("the registration is refused because the weight is not allowed")]
    public void ThenRefusedForWeight()
    {
        var invalid = Assert.IsType<CommandValidationException>(world.LastError);
        Assert.Contains(invalid.Errors, e => e.PropertyName == nameof(ShipmentDetails.WeightKg));
    }

    [Then(@"^there is exactly (\d+) parcels? with tracking number ""(APX-\d+)""$")]
    public async Task ThenThereIsExactlyNParcels(int expected, string tn)
    {
        var all = await world.Tracking.ListAsync(new TrackingListQuery(PageSize: TrackingLimits.MaxPageSize), CancellationToken.None);
        Assert.Equal(expected, all.Items.Count(p => p.TrackingNumber == tn));
    }

    [Then(@"^there is no parcel with tracking number ""(APX-\d+)""$")]
    public Task ThenThereIsNoParcel(string tn) =>
        Assert.ThrowsAsync<EntityNotFoundException>(() => world.Tracking.GetAsync(tn, CancellationToken.None));

    [Then(@"^the heavy-parcel surcharge is ([\d.]+)$")]
    public void ThenTheSurchargeIs(decimal expected)
    {
        Assert.NotNull(world.LastCharges);
        Assert.Equal(expected, world.LastCharges.HeavyParcelSurcharge);
    }
}
