using ApexPost.Courier.Core.Sync;

namespace ApexPost.Courier;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly CancellationTokenSource _stop = new();

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_services.GetRequiredService<AppShell>());

        // Background sync: every 30 s, sooner when a scan is saved or the network comes back. Saving a scan never waits for it.
        window.Created += (_, _) =>
        {
            var sync = _services.GetRequiredService<SyncCoordinator>();
            _ = Task.Run(() => sync.RunAsync(TimeSpan.FromSeconds(30), _stop.Token));
        };
        window.Destroying += (_, _) => _stop.Cancel();
        return window;
    }
}
