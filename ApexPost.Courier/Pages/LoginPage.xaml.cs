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

    private async void OnGetToken(object? sender, EventArgs e)
    {
        var courierId = CourierEntry.Text?.Trim() ?? string.Empty;
        if (courierId.Length == 0)
        {
            ErrorLabel.Text = "Enter a courier ID, for example C-1001.";
            return;
        }

        await RunAsync(async () =>
        {
            var token = await _api.GetDevelopmentTokenAsync(courierId, CancellationToken.None);
            await CompleteSignInAsync(token, courierId);
        });
    }

    private async void OnUsePastedToken(object? sender, EventArgs e)
    {
        var token = TokenEditor.Text?.Trim() ?? string.Empty;
        if (token.Split('.').Length != 3)
        {
            ErrorLabel.Text = "That does not look like a JWT (three parts separated by dots).";
            return;
        }

        await RunAsync(() => CompleteSignInAsync(token, CourierEntry.Text?.Trim() ?? string.Empty));
    }

    /// <summary>One courier id everywhere: the token's courierId claim wins; otherwise the id typed here. New Scan and the scans stored by Part A use it.</summary>
    private async Task CompleteSignInAsync(string token, string typedCourierId)
    {
        var courierId = JwtClaims.GetCourierId(token) ?? typedCourierId;
        if (string.IsNullOrWhiteSpace(courierId))
        {
            throw new ApiException(System.Net.HttpStatusCode.BadRequest, "The token has no courierId claim: type your courier ID too.");
        }

        await _tokens.SetAsync(token);
        await _tokens.SetCourierIdAsync(courierId);
        TokenEditor.Text = string.Empty;
        _sync.NotifyScanSaved(); // scans saved while signed out can go now
        await Shell.Current.GoToAsync("//Main/NewScan");
    }

    private async Task RunAsync(Func<Task> action)
    {
        ErrorLabel.Text = string.Empty;
        Busy.IsRunning = true;
        SignInButton.IsEnabled = false;
        PasteButton.IsEnabled = false;
        try
        {
            await action();
        }
        catch (ApiException ex)
        {
            ErrorLabel.Text = ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            ErrorLabel.Text = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException || ex.GetType().Name is "BrokenCircuitException" or "TimeoutRejectedException")
        {
            ErrorLabel.Text = "Cannot reach the server. Check the connection and try again.";
        }
        finally
        {
            Busy.IsRunning = false;
            SignInButton.IsEnabled = true;
            PasteButton.IsEnabled = true;
        }
    }
}
