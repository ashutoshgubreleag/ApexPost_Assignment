namespace ApexPost.Specs.Support;

/// <summary>
/// Scenario lifecycle. BeforeScenario gives every scenario a brand-new world (empty store, empty phone, fixed clock);
/// AfterScenario clears what the scenario created. Together with context injection this makes scenarios independent
/// of each other and of execution order.
/// </summary>
[Binding]
public sealed class Hooks(ShipmentWorld world)
{
    /// <summary>The default business day; a Background or step can move it ("today is ...").</summary>
    public static readonly DateTimeOffset DefaultToday = new(2026, 12, 15, 8, 0, 0, TimeSpan.Zero);

    [BeforeScenario]
    public void CreateFreshWorld() => world.Start(DefaultToday);

    [AfterScenario]
    public void CleanUp() => world.Clear();
}
