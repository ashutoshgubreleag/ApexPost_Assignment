using ApexPost.Courier.Core.Auth;
using ApexPost.Courier.Core.Sync;

namespace ApexPost.Courier.Pages;

public partial class SyncStatusPage : ContentPage
{
    private readonly SyncCoordinator _sync;
    private readonly ITokenStore _tokens;

    public SyncStatusPage(SyncCoordinator sync, ITokenStore tokens)
    {
        InitializeComponent();
        _sync = sync;
        _tokens = tokens;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Banner.Bind(_sync);
        _sync.Changed += OnChanged;
        Apply(_sync.Current);
    }

    protected override void OnDisappearing()
    {
        _sync.Changed -= OnChanged;
        Banner.Unbind();
        base.OnDisappearing();
    }

    private void OnChanged(object? sender, SyncSnapshot s) => MainThread.BeginInvokeOnMainThread(() => Apply(s));

    private void Apply(SyncSnapshot s)
    {
        NetworkLabel.Text = $"Network: {(s.IsOnline ? "online" : "offline")}{(s.IsSyncing ? " (syncing…)" : string.Empty)}";
        CircuitLabel.Text = $"Server connection: {s.Circuit}";
        PendingLabel.Text = $"Waiting to upload: {s.PendingCount}";
        LastAttemptLabel.Text = $"Last attempt: {Format(s.LastAttemptUtc)}";
        LastSuccessLabel.Text = $"Last successful sync: {Format(s.LastSuccessUtc)}";
        MessageLabel.Text = s.LastMessage ?? string.Empty;
    }

    private static string Format(DateTimeOffset? t) => t is { } v ? v.ToLocalTime().ToString("g") : "never";

    private async void OnSyncNow(object? sender, EventArgs e)
    {
        SyncButton.IsEnabled = false;
        try
        {
            await _sync.SyncNowAsync();
        }
        finally
        {
            SyncButton.IsEnabled = true;
        }
    }

    private async void OnSignOut(object? sender, EventArgs e)
    {
        await _tokens.ClearAsync();
        await Shell.Current.GoToAsync("//Login");
    }
}
