using System.Globalization;
using ApexPost.Specs.Support;

namespace ApexPost.Specs.StepDefinitions;

/// <summary>Steps used by more than one feature. State lives in the injected <see cref="ShipmentWorld"/>.</summary>
[Binding]
public sealed class CommonSteps(ShipmentWorld world)
{
    [Given(@"^today is (\d{1,2} [A-Z][a-z]+ \d{4})$")]
    public void GivenTodayIs(string day)
    {
        var date = DateTime.ParseExact(day, "d MMMM yyyy", CultureInfo.InvariantCulture);
        world.SetToday(new DateTimeOffset(date.Year, date.Month, date.Day, 8, 0, 0, TimeSpan.Zero));
    }
}
