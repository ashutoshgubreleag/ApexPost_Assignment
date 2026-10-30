using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;

namespace ApexPost.Models.Domain;

/// <summary>A post office branch. Aggregate root for its counters.</summary>
public sealed class Branch : Entity
{
    private readonly List<Counter> _counters = [];

    private Branch()
    {
        // For EF Core materialisation.
    }

    public BranchCode Code { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Region { get; private set; } = string.Empty;

    /// <summary>Human-readable opening hours, e.g. "Mon-Fri 08:30-17:30; Sat 09:00-12:00".</summary>
    public string OpeningHours { get; private set; } = string.Empty;

    public BranchStatus Status { get; private set; } = BranchStatus.Active;

    public IReadOnlyCollection<Counter> Counters => _counters.AsReadOnly();

    public bool IsOpen => Status == BranchStatus.Active;

    public static Branch Create(BranchCode code, string name, string region, string openingHours, int counters = 1)
    {
        if (string.IsNullOrEmpty(code.Value))
        {
            throw new ArgumentException("Branch code is required.", nameof(code));
        }

        var branch = new Branch { Code = code };
        branch.UpdateDetails(name, region, openingHours);
        for (var i = 1; i <= Math.Max(1, counters); i++)
        {
            branch.AddCounter($"Counter {i}");
        }

        return branch;
    }

    public void UpdateDetails(string name, string region, string openingHours)
    {
        Name = Guard.Text(name, nameof(name), 100);
        Region = Guard.OptionalText(region, nameof(region), 60);
        OpeningHours = Guard.OptionalText(openingHours, nameof(openingHours), 120);
    }

    public void SetStatus(BranchStatus status) => Status = Guard.Defined(status, nameof(status));

    public Counter AddCounter(string name)
    {
        var number = _counters.Count == 0 ? 1 : _counters.Max(c => c.Number) + 1;
        var counter = new Counter(Code, number, name);
        _counters.Add(counter);
        return counter;
    }

    /// <summary>Rule: a closed branch cannot accept transactions.</summary>
    public void EnsureOpenForTrading()
    {
        if (!IsOpen)
        {
            throw new BusinessRuleViolationException("BRANCH_CLOSED", $"Branch {Code} is closed and cannot accept transactions.");
        }
    }

    public void EnsureActiveCounter(int counterNumber)
    {
        if (!_counters.Any(c => c.Number == counterNumber && c.IsActive))
        {
            throw new BusinessRuleViolationException("COUNTER_INVALID", $"Counter {counterNumber} is not an active terminal at {Code}.");
        }
    }
}

/// <summary>A counter position inside a branch. Identified within the branch by its number.</summary>
public sealed class Counter : Entity
{
    private Counter()
    {
        // For EF Core materialisation.
    }

    internal Counter(BranchCode branchCode, int number, string name)
    {
        BranchCode = branchCode;
        Number = number;
        Name = Guard.Text(name, nameof(name), 50);
    }

    public BranchCode BranchCode { get; private set; }

    public int Number { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public void Deactivate() => IsActive = false;
}

/// <summary>Lookup row for <see cref="StaffRole"/> (the Roles table). The key is the enum itself.</summary>
public sealed class Role
{
    private Role()
    {
    }

    public Role(StaffRole id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
    }

    public StaffRole Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;
}

/// <summary>Lookup row for <see cref="ProductCategory"/> (the ProductCategories table).</summary>
public sealed class Category
{
    private Category()
    {
    }

    public Category(ProductCategory id, string name)
    {
        Id = id;
        Name = name;
    }

    public ProductCategory Id { get; private set; }

    public string Name { get; private set; } = string.Empty;
}
