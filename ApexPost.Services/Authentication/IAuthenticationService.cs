using ApexPost.Services.Abstractions;

namespace ApexPost.Services.Authentication;

public interface IAuthenticationService
{
    /// <summary>Returns the signed-in staff member, or null for a wrong password or inactive account.</summary>
    Task<StaffDto?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken);
}
