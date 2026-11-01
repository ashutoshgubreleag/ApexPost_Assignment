using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;

namespace ApexPost.Models.Domain;

/// <summary>A member of staff. Email is unique and normalised; the password is only ever a hash.</summary>
public sealed class StaffMember : Entity
{
    private StaffMember()
    {
        // For EF Core materialisation.
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Lower-case so the UNIQUE constraint is effectively case-insensitive.</summary>
    public string Email { get; private set; } = string.Empty;

    public StaffRole Role { get; private set; }

    public BranchCode HomeBranchCode { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>PBKDF2 hash ("PBKDF2$iterations$salt$hash"); never the password itself.</summary>
    public string PasswordHash { get; private set; } = string.Empty;

    public static StaffMember Create(string name, string email, StaffRole role, BranchCode homeBranch, string passwordHash)
    {
        var staff = new StaffMember();
        staff.UpdateDetails(name, email, role, homeBranch);
        staff.SetPasswordHash(passwordHash);
        return staff;
    }

    public void UpdateDetails(string name, string email, StaffRole role, BranchCode homeBranch)
    {
        Name = Guard.Text(name, nameof(name), 100);
        Email = NormaliseEmail(email);
        Role = Guard.Defined(role, nameof(role));
        HomeBranchCode = homeBranch;
    }

    public void SetPasswordHash(string passwordHash) =>
        PasswordHash = Guard.Text(passwordHash, nameof(passwordHash), 200);

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    /// <summary>
    /// Rules: inactive staff cannot transact; only clerks and branch managers work a counter; and
    /// they only transact at their assigned branch (a forged branch claim fails here too).
    /// </summary>
    public void EnsureCanTransactAt(BranchCode branch)
    {
        if (!IsActive)
        {
            throw new BusinessRuleViolationException("STAFF_INACTIVE", "Inactive staff cannot open or process transactions.");
        }

        if (Role is not (StaffRole.CounterClerk or StaffRole.BranchManager))
        {
            throw new ForbiddenOperationException("Only counter clerks and branch managers can process counter transactions.");
        }

        if (HomeBranchCode != branch)
        {
            throw new BusinessRuleViolationException("WRONG_BRANCH", $"You are assigned to {HomeBranchCode} and cannot transact at {branch}.");
        }
    }

    public static string NormaliseEmail(string email)
    {
        var normalised = Guard.Text(email, nameof(email), 254).ToLowerInvariant();
        return normalised.Contains('@', StringComparison.Ordinal)
            ? normalised
            : throw new ArgumentException("Email must contain '@'.", nameof(email));
    }
}
