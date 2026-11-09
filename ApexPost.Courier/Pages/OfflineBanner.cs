using ApexPost.Courier.Core.Resilience;
using ApexPost.Courier.Core.Sync;

namespace ApexPost.Courier.Pages;

/// <summary>Top-of-page strip: visible when the phone is offline or the server is being given a rest (circuit open).</summary>
public sealed class OfflineBanner : ContentView
{
    private readonly Label _label = new() { TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, Padding = 6 };
    private readonly Border _frame = new() { StrokeThickness = 0 };
    private SyncCoordinator? _sync;

    public OfflineBanner()
    {
        _frame.Content = _label;
        Content = _frame;
        IsVisible = false;
    }

    public void Bind(SyncCoordinator sync)
    {
        if (_sync is not null)
        {
            _sync.Changed -= OnChanged;
        }

        _sync = sync;
        _sync.Changed += OnChanged;
        Apply(sync.Current);
    }

    public void Unbind()
    {
        if (_sync is not null)
        {
            _sync.Changed -= OnChanged;
            _sync = null;
        }
    }

    private void OnChanged(object? sender, SyncSnapshot s) => MainThread.BeginInvokeOnMainThread(() => Apply(s));

    private void Apply(SyncSnapshot s)
    {
        if (!s.IsOnline)
        {
            Show($"Offline: scans are saved on this phone ({s.PendingCount} waiting).", Colors.DarkOrange);
        }
        else if (s.Circuit == CircuitStateKind.Open)
        {
            Show($"Server unavailable: will retry shortly ({s.PendingCount} waiting).", Colors.Firebrick);
        }
        else
        {
            IsVisible = false;
        }
    }

    private void Show(string text, Color color)
    {
        _label.Text = text;
        _frame.BackgroundColor = color;
        IsVisible = true;
    }
}
