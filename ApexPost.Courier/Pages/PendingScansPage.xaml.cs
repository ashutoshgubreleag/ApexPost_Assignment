using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Pages;

public partial class PendingScansPage : ContentPage
{
    private readonly IScanRepository _scans;
    private readonly SyncCoordinator _sync;

    public PendingScansPage(IScanRepository scans, SyncCoordinator sync)
    {
        InitializeComponent();
        _scans = scans;
        _sync = sync;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Banner.Bind(_sync);
        _sync.Changed += OnSyncChanged;
        Refresh();
    }

    protected override void OnDisappearing()
    {
        _sync.Changed -= OnSyncChanged;
        Banner.Unbind();
        base.OnDisappearing();
    }

    private void OnSyncChanged(object? sender, SyncSnapshot e) => MainThread.BeginInvokeOnMainThread(Refresh);

    private void Refresh()
    {
        // Pending ones plus the ones the server rejected (Failed), newest first. Synced scans are history, not shown.
        var rows = _scans.ListAll()
            .Where(s => s.SyncStatus != SyncStatus.Synced)
            .OrderByDescending(s => s.ScannedAtUtc)
            .Select(s => new ScanRow(
                $"{s.TrackingNumber}  {s.ScanType}",
                $"{s.Location} · {s.ScannedAtUtc.ToLocalTime():g} · {s.SyncStatus}"))
            .ToList();
        List.ItemsSource = rows;
        Header.Text = $"{_scans.CountPending()} waiting to upload";
    }

    private sealed record ScanRow(string Title, string Detail);
}
