using System.Text.RegularExpressions;
using ApexPost.Courier.Core.Auth;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Pages;

public partial class NewScanPage : ContentPage
{
    private static readonly Regex TrackingPattern = new(@"^APX-\d{8}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IScanRepository _scans;
    private readonly ITokenStore _tokens;
    private readonly SyncCoordinator _sync;
    private string _courierId = "unknown";

    public NewScanPage(IScanRepository scans, ITokenStore tokens, SyncCoordinator sync)
    {
        InitializeComponent();
        _scans = scans;
        _tokens = tokens;
        _sync = sync;
        TypePicker.ItemsSource = Enum.GetNames<ScanType>();
        TypePicker.SelectedIndex = 0;
        TypePicker.SelectedIndexChanged += (_, _) => NotesHint.IsVisible = TypePicker.SelectedItem as string == nameof(ScanType.Delivered);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Banner.Bind(_sync);
        _courierId = JwtClaims.GetCourierId(await _tokens.GetAsync()) ?? await _tokens.GetCourierIdAsync() ?? "unknown";
        CourierLabel.Text = $"Courier: {_courierId}";
    }

    protected override void OnDisappearing()
    {
        Banner.Unbind();
        base.OnDisappearing();
    }

    /// <summary>Offline-first: the scan is written to the local database and the page answers at once. Upload is the sync loop's job.</summary>
    private void OnSave(object? sender, EventArgs e)
    {
        var tracking = TrackingEntry.Text?.Trim() ?? string.Empty;
        if (!TrackingPattern.IsMatch(tracking))
        {
            ResultLabel.TextColor = Colors.Firebrick;
            ResultLabel.Text = "Tracking number must look like APX-12345678.";
            return;
        }

        if (string.IsNullOrWhiteSpace(LocationEntry.Text))
        {
            ResultLabel.TextColor = Colors.Firebrick;
            ResultLabel.Text = "Location is required.";
            return;
        }

        var type = Enum.Parse<ScanType>((string)TypePicker.SelectedItem);
        if (type == ScanType.Delivered && string.IsNullOrWhiteSpace(NotesEntry.Text))
        {
            ResultLabel.TextColor = Colors.Firebrick;
            ResultLabel.Text = "A Delivered scan needs the recipient's signature name in Notes.";
            return;
        }

        var scan = _scans.Create(ShipmentScan.CreateNew(tracking, type, _courierId, LocationEntry.Text!, DateTimeOffset.UtcNow, NotesEntry.Text));
        _sync.NotifyScanSaved();

        ResultLabel.TextColor = Colors.SeaGreen;
        ResultLabel.Text = _sync.Current.IsOnline
            ? $"Saved ({scan.ScanId.ToString()[..8]}). Uploading in the background."
            : $"Saved offline ({scan.ScanId.ToString()[..8]}). It will upload when the network is back.";
        NotesEntry.Text = string.Empty;
    }
}
