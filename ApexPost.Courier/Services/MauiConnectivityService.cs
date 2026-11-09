using ApexPost.Courier.Core.Sync;

namespace ApexPost.Courier.Services;

/// <summary>Adapts MAUI's Connectivity to the Core's INetworkStatus. "Online" means any network path with internet access.</summary>
public sealed class MauiConnectivityService : INetworkStatus, IDisposable
{
    public MauiConnectivityService() => Connectivity.Current.ConnectivityChanged += OnChanged;

    public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public event EventHandler<bool>? OnlineChanged;

    public void Dispose() => Connectivity.Current.ConnectivityChanged -= OnChanged;

    private void OnChanged(object? sender, ConnectivityChangedEventArgs e) => OnlineChanged?.Invoke(this, IsOnline);
}
