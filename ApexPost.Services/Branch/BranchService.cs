using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.Validation;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Common;

namespace ApexPost.Services.Branch;

/// <summary>Branch maintenance. Reads use <see cref="IBranchQueries"/>; writes use the repository and one commit.</summary>
public sealed class BranchService(IBranchRepository branches, IBranchQueries queries, IUnitOfWork unitOfWork) : IBranchService
{
    public Task<IReadOnlyList<BranchDto>> GetAllAsync(CancellationToken cancellationToken) => queries.ListAsync(cancellationToken);

    public async Task<BranchDto> GetByCodeAsync(BranchCode branchCode, CancellationToken cancellationToken) =>
        await queries.GetAsync(branchCode, cancellationToken).ConfigureAwait(false)
        ?? throw new EntityNotFoundException("Branch", branchCode);

    public async Task<BranchDto> CreateAsync(SaveBranchCommand command, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureAdministrator(actor);
        ReflectionValidator.EnsureValid(command);

        var code = new BranchCode(command.BranchCode);
        if (await branches.ExistsAsync(code, cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessRuleViolationException("BRANCH_EXISTS", $"Branch {code} already exists.");
        }

        // Explicit mapping: command -> domain factory. Status always starts Active, whatever was posted.
        var branch = Models.Domain.Branch.Create(code, command.Name, command.Region, command.OpeningHours, command.CounterCount);
        branches.Add(branch);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return branch.ToDto();
    }

    public async Task<BranchDto> UpdateAsync(SaveBranchCommand command, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureAdministrator(actor);
        ReflectionValidator.EnsureValid(command);

        var branch = await LoadAsync(new BranchCode(command.BranchCode), cancellationToken).ConfigureAwait(false);
        branch.UpdateDetails(command.Name, command.Region, command.OpeningHours);

        // Grow the counter list without renumbering existing terminals.
        while (branch.Counters.Count < command.CounterCount)
        {
            branch.AddCounter($"Counter {branch.Counters.Count + 1}");
        }

        foreach (var counter in branch.Counters.Where(c => c.Number > command.CounterCount && c.IsActive))
        {
            counter.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return branch.ToDto();
    }

    public async Task SetStatusAsync(BranchCode branchCode, BranchStatus status, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.IsAdministrator && !(actor.IsBranchManager && actor.BranchCode == branchCode))
        {
            throw new ForbiddenOperationException("Only an administrator or the branch's manager can open or close a branch.");
        }

        var branch = await LoadAsync(branchCode, cancellationToken).ConfigureAwait(false);
        branch.SetStatus(status);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Models.Domain.Branch> LoadAsync(BranchCode code, CancellationToken cancellationToken) =>
        await branches.GetByCodeAsync(code, cancellationToken).ConfigureAwait(false)
        ?? throw new EntityNotFoundException("Branch", code);

    private static void EnsureAdministrator(StaffContext actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.IsAdministrator)
        {
            throw new ForbiddenOperationException("Only administrators can create or edit branches.");
        }
    }
}
