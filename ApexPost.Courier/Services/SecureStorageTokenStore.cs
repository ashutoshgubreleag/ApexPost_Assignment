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
    private string? _memory;

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
        try
        {
            SecureStorage.Default.Remove(Key);
        }
        catch (Exception ex) when (IsStoreProblem(ex))
        {
            // nothing stored
        }

        return Task.CompletedTask;
    }

    private static bool IsStoreProblem(Exception ex) => ex is not OperationCanceledException;
}
