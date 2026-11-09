using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Core.Auth;
using ApexPost.Courier.Core.Sync;

namespace ApexPost.Courier.Pages;

public partial class LoginPage : ContentPage
{
    private readonly ApexApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly SyncCoordinator _sync;

    public LoginPage(ApexApiClient api, ITokenStore tokens, SyncCoordinator sync)
    {
        InitializeComponent();
        _api = api;
        _tokens = tokens;
        _sync = sync;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Banner.Bind(_sync);

        // Already signed in (token in SecureStorage and not expired): skip this page.
        var token = await _tokens.GetAsync();
        if (token is not null && (JwtClaims.GetExpiry(token) is not { } exp || exp > DateTimeOffset.UtcNow))
        {
            await Shell.Current.GoToAsync("//Main/NewScan");
        }
    }

    protected override void OnDisappearing()
    {
        Banner.Unbind();
        base.OnDisappearing();
    }

    private async void OnSignIn(object? sender, EventArgs e)
    {
        ErrorLabel.Text = string.Empty;
        Busy.IsRunning = true;
        SignInButton.IsEnabled = false;
        try
        {
            var token = await _api.LoginAsync(UserEntry.Text?.Trim() ?? string.Empty, PasswordEntry.Text ?? string.Empty, CancellationToken.None);
            await _tokens.SetAsync(token);
            PasswordEntry.Text = string.Empty;
            _sync.NotifyScanSaved(); // anything saved while signed out can go now
            await Shell.Current.GoToAsync("//Main/NewScan");
        }
        catch (UnauthorizedAccessException ex)
        {
            ErrorLabel.Text = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiException or TimeoutException || ex.GetType().Name is "BrokenCircuitException" or "TimeoutRejectedException")
        {
            ErrorLabel.Text = "Cannot reach the server. Check the connection and try again.";
        }
        finally
        {
            Busy.IsRunning = false;
            SignInButton.IsEnabled = true;
        }
    }
}
