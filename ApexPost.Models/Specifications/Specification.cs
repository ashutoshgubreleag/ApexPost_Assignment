namespace ApexPost.Models.Specifications;

/// <summary>A reusable, composable filter over entities.</summary>
public interface ISpecification<in T>
{
    bool IsSatisfiedBy(T candidate);
}

/// <summary>
/// Specification pattern. Report criteria such as "paid transactions at BR-001 on 24 Oct"
/// are composed from small named specifications instead of ad-hoc lambdas in every report,
/// and the same objects can later be translated into a SQL WHERE clause.
/// </summary>
public abstract class Specification<T> : ISpecification<T> where T : class
{
    public static Specification<T> All { get; } = new PredicateSpecification<T>(_ => true, "all");

    public abstract bool IsSatisfiedBy(T candidate);

    /// <summary>Wraps a delegate as a specification.</summary>
    public static Specification<T> Where(Func<T, bool> predicate, string description = "custom") =>
        new PredicateSpecification<T>(predicate, description);

    public Specification<T> And(ISpecification<T> other) =>
        new PredicateSpecification<T>(c => IsSatisfiedBy(c) && other.IsSatisfiedBy(c), "and");

    public Specification<T> Or(ISpecification<T> other) =>
        new PredicateSpecification<T>(c => IsSatisfiedBy(c) || other.IsSatisfiedBy(c), "or");

    public Specification<T> Not() =>
        new PredicateSpecification<T>(c => !IsSatisfiedBy(c), "not");

    public static Specification<T> operator &(Specification<T> left, Specification<T> right) => left.And(right);
    public static Specification<T> operator |(Specification<T> left, Specification<T> right) => left.Or(right);
    public static Specification<T> operator !(Specification<T> spec) => spec.Not();
}

internal sealed class PredicateSpecification<T>(Func<T, bool> predicate, string description) : Specification<T> where T : class
{
    public string Description { get; } = description;

    public override bool IsSatisfiedBy(T candidate) => predicate(candidate);

    public override string ToString() => Description;
}
