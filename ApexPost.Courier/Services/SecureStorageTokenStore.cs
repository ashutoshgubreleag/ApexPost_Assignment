using ApexPost.Courier.Core.Auth;

namespace ApexPost.Courier.Services;

/// <summary>
/// Keeps the JWT in the platform's secure store (Android Keystore-backed storage, Windows data protection),
/// never in Preferences, files or the Couchbase database. If the secure store is unavailable (some unpackaged
/// Windows setups) the token is kept in memory only: the user signs in again next launch, and nothing is written in plain text.
/// </summary>
public sealed class SecureStorageTokenStore : ITokenStore
{
    private const string Key = "apexpost.jwt";
    private const string CourierKey = "apexpost.courierId";
    private string? _memory;
    private string? _memoryCourier;

    public async Task<string?> GetAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(Key).ConfigureAwait(false) ?? _memory;
        }
        catch (Exception ex) when (IsStoreProblem(ex))
        {
            return _memory;
        }
    }

    public async Task SetAsync(string token)
    {
        _memory = token;
        try
        {
            await SecureStorage.Default.SetAsync(Key, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsStoreProblem(ex))
        {
            // memory fallback only
        }
    }

    public Task ClearAsync()
    {
        _memory = null;
        _memoryCourier = null;
        try
        {
            SecureStorage.Default.Remove(Key);
            SecureStorage.Default.Remove(CourierKey);
        }
        catch (Exception ex) when (IsStoreProblem(ex))
        {
            // nothing stored
        }

        return Task.CompletedTask;
    }

    public async Task<string?> GetCourierIdAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(CourierKey).ConfigureAwait(false) ?? _memoryCourier;
        }
        catch (Exception ex) when (IsStoreProblem(ex))
        {
            return _memoryCourier;
        }
    }

    public async Task SetCourierIdAsync(string courierId)
    {
        _memoryCourier = courierId;
        try
        {
            await SecureStorage.Default.SetAsync(CourierKey, courierId).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsStoreProblem(ex))
        {
            // memory fallback only
        }
    }

    private static bool IsStoreProblem(Exception ex) => ex is not OperationCanceledException;
}
