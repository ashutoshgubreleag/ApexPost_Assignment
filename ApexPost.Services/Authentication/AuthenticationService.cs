using ApexPost.Services.Abstractions;
using ApexPost.Services.Common;

namespace ApexPost.Services.Authentication;

public sealed class AuthenticationService(IStaffRepository staff, IPasswordHasher hasher) : IAuthenticationService
{
    public async Task<StaffDto?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var candidate = await staff.GetByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken).ConfigureAwait(false);

        // Always run the hash (dummy hash for unknown users) to avoid a timing side channel.
        var passwordOk = hasher.Verify(password, candidate?.PasswordHash ?? string.Empty);

        // Inactive staff cannot sign in at all.
        return passwordOk && candidate is { IsActive: true } ? candidate.ToDto() : null;
    }
}
