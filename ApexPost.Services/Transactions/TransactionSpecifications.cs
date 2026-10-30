using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Models.Specifications;

namespace ApexPost.Services.Transactions;

/// <summary>Named, composable filters used by reports and the dashboard.</summary>
public static class TransactionSpecifications
{
    public static Specification<CounterTransaction> AtBranch(BranchCode branch) =>
        Specification<CounterTransaction>.Where(t => t.BranchCode == branch, $"branch {branch}");

    public static Specification<CounterTransaction> WithStatus(TransactionStatus status) =>
        Specification<CounterTransaction>.Where(t => t.Status == status, $"status {status}");

    /// <summary>Completed (paid) on the given UTC calendar day.</summary>
    public static Specification<CounterTransaction> ClosedOn(DateOnly day) =>
        Specification<CounterTransaction>.Where(t => t.ClosedAt is { } closed && DateOnly.FromDateTime(closed.UtcDateTime) == day, $"closed on {day:yyyy-MM-dd}");

    public static Specification<CounterTransaction> PaidOn(DateOnly day) => WithStatus(TransactionStatus.Paid) & ClosedOn(day);
}
