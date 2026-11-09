namespace ApexPost.Courier.Core.Auth;

/// <summary>Where the JWT lives. The MAUI app uses SecureStorage; tests use memory.</summary>
public interface ITokenStore
{
    Task<string?> GetAsync();

    Task SetAsync(string token);

    Task ClearAsync();

    /// <summary>The courier id chosen at sign-in (kept next to the token; used when the token has no readable courierId claim).</summary>
    Task<string?> GetCourierIdAsync();

    Task SetCourierIdAsync(string courierId);
}

public sealed class InMemoryTokenStore : ITokenStore
{
    private string? _token;
    private string? _courierId;

    public Task<string?> GetAsync() => Task.FromResult(_token);

    public Task SetAsync(string token)
    {
        _token = token;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _token = null;
        _courierId = null;
        return Task.CompletedTask;
    }

    public Task<string?> GetCourierIdAsync() => Task.FromResult(_courierId);

    public Task SetCourierIdAsync(string courierId)
    {
        _courierId = courierId;
        return Task.CompletedTask;
    }
}
