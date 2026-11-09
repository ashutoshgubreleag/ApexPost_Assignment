namespace ApexPost.Courier.Core.Auth;

/// <summary>Where the JWT lives. The MAUI app uses SecureStorage; tests use memory.</summary>
public interface ITokenStore
{
    Task<string?> GetAsync();

    Task SetAsync(string token);

    Task ClearAsync();
}

public sealed class InMemoryTokenStore : ITokenStore
{
    private string? _token;

    public Task<string?> GetAsync() => Task.FromResult(_token);

    public Task SetAsync(string token)
    {
        _token = token;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _token = null;
        return Task.CompletedTask;
    }
}
