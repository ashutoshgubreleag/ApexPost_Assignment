namespace ApexPost.Courier.Core.Sync;

/// <summary>Is the device online? MAUI Connectivity implements it in the app; tests use a fake.</summary>
public interface INetworkStatus
{
    bool IsOnline { get; }

    event EventHandler<bool>? OnlineChanged;
}
