using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Models.Validation;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Common;

namespace ApexPost.Services.Branch;

public sealed record SaveBranchCommand
{
    [NotBlank, Matches("^BR-[0-9]{3}$", "in the form BR-000")]
    public string BranchCode { get; init; } = string.Empty;

    [NotBlank, MaxLengthRule(80)]
    public string Name { get; init; } = string.Empty;

    [NotBlank, MaxLengthRule(40)]
    public string Region { get; init; } = string.Empty;

    [NotBlank, MaxLengthRule(120)]
    public string OpeningHours { get; init; } = string.Empty;

    [InRange(1, 20)]
    public int CounterCount { get; init; } = 2;
}

public interface IBranchService
{
    Task<IReadOnlyList<BranchDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<BranchDto> GetByCodeAsync(BranchCode branchCode, CancellationToken cancellationToken);

    Task<BranchDto> CreateAsync(SaveBranchCommand command, StaffContext actor, CancellationToken cancellationToken);

    Task<BranchDto> UpdateAsync(SaveBranchCommand command, StaffContext actor, CancellationToken cancellationToken);

    Task SetStatusAsync(BranchCode branchCode, BranchStatus status, StaffContext actor, CancellationToken cancellationToken);
}
