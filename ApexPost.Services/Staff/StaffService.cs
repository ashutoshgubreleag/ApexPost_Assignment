using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.Validation;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Authentication;
using ApexPost.Services.Common;

namespace ApexPost.Services.Staff;

public sealed class StaffService(
    IStaffRepository staff,
    IStaffQueries queries,
    IBranchRepository branches,
    IUnitOfWork unitOfWork,
    IPasswordHasher hasher) : IStaffService
{
    private const int MinimumPasswordLength = 10;

    public Task<IReadOnlyList<StaffDto>> GetAllAsync(StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return queries.ListAsync(actor.IsAdministrator || actor.IsAuditor ? null : actor.BranchCode, cancellationToken);
    }

    public async Task<StaffDto> GetByIdAsync(int staffId, CancellationToken cancellationToken) =>
        await queries.GetAsync(staffId, cancellationToken).ConfigureAwait(false)
        ?? throw new EntityNotFoundException("Staff member", staffId);

    public async Task<StaffDto> CreateAsync(SaveStaffCommand command, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ReflectionValidator.EnsureValid(command);
        var branch = new BranchCode(command.HomeBranchCode);
        EnsureCanManage(actor, branch, command.Role);
        await EnsureBranchExistsAsync(branch, cancellationToken).ConfigureAwait(false);
        var email = StaffMember.NormaliseEmail(command.Email);
        await EnsureEmailUniqueAsync(email, 0, cancellationToken).ConfigureAwait(false);

        if (command.InitialPassword is not { Length: >= MinimumPasswordLength })
        {
            throw new CommandValidationException(nameof(command.InitialPassword), $"Initial password must be at least {MinimumPasswordLength} characters.");
        }

        // Explicit mapping: the command's Id is ignored on create (the database assigns it).
        var member = StaffMember.Create(command.Name, email, command.Role, branch, hasher.Hash(command.InitialPassword));
        if (!command.IsActive)
        {
            member.Deactivate();
        }

        staff.Add(member);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return member.ToDto();
    }

    public async Task<StaffDto> UpdateAsync(SaveStaffCommand command, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        ReflectionValidator.EnsureValid(command);

        var existing = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        var branch = new BranchCode(command.HomeBranchCode);
        EnsureCanManage(actor, existing.HomeBranchCode, existing.Role);
        EnsureCanManage(actor, branch, command.Role);
        await EnsureBranchExistsAsync(branch, cancellationToken).ConfigureAwait(false);
        var email = StaffMember.NormaliseEmail(command.Email);
        await EnsureEmailUniqueAsync(email, command.Id, cancellationToken).ConfigureAwait(false);

        if (existing.Id == actor.StaffId && !command.IsActive)
        {
            throw new BusinessRuleViolationException("SELF_DEACTIVATION", "You cannot deactivate your own account.");
        }

        existing.UpdateDetails(command.Name, email, command.Role, branch);
        if (command.IsActive)
        {
            existing.Activate();
        }
        else
        {
            existing.Deactivate();
        }

        if (command.InitialPassword is { Length: > 0 } password)
        {
            existing.SetPasswordHash(password.Length >= MinimumPasswordLength
                ? hasher.Hash(password)
                : throw new CommandValidationException(nameof(command.InitialPassword), $"Password must be at least {MinimumPasswordLength} characters."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return existing.ToDto();
    }

    public async Task DeactivateAsync(int staffId, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var existing = await LoadAsync(staffId, cancellationToken).ConfigureAwait(false);
        EnsureCanManage(actor, existing.HomeBranchCode, existing.Role);
        if (existing.Id == actor.StaffId)
        {
            throw new BusinessRuleViolationException("SELF_DEACTIVATION", "You cannot deactivate your own account.");
        }

        if (!existing.IsActive)
        {
            return;
        }

        existing.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<StaffMember> LoadAsync(int staffId, CancellationToken cancellationToken) =>
        await staff.GetByIdAsync(staffId, cancellationToken).ConfigureAwait(false)
        ?? throw new EntityNotFoundException("Staff member", staffId);

    private static void EnsureCanManage(StaffContext actor, BranchCode branch, StaffRole targetRole)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var allowed = actor.Role switch
        {
            StaffRole.Administrator => true,
            StaffRole.BranchManager => actor.BranchCode == branch && targetRole == StaffRole.CounterClerk,
            _ => false,
        };

        if (!allowed)
        {
            throw new ForbiddenOperationException("You are not allowed to manage this staff member.");
        }
    }

    private async Task EnsureBranchExistsAsync(BranchCode branch, CancellationToken cancellationToken)
    {
        if (!await branches.ExistsAsync(branch, cancellationToken).ConfigureAwait(false))
        {
            throw new CommandValidationException(nameof(SaveStaffCommand.HomeBranchCode), $"Branch {branch} does not exist.");
        }
    }

    private async Task EnsureEmailUniqueAsync(string email, int ignoreId, CancellationToken cancellationToken)
    {
        if (await staff.EmailInUseAsync(email, ignoreId, cancellationToken).ConfigureAwait(false))
        {
            throw new CommandValidationException(nameof(SaveStaffCommand.Email), $"A staff member with email '{email}' already exists.");
        }
    }
}
