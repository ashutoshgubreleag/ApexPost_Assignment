using ApexPost.Models.Validation;

namespace ApexPost.Models.Exceptions;

/// <summary>
/// Base type for expected business failures. Living in the Models project lets the
/// service layer throw them and the web layer (filters, exception handler) map them to
/// HTTP results, without the web layer referencing service internals.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

/// <summary>A business rule was broken (insufficient stock, branch closed, ...).</summary>
public sealed class BusinessRuleViolationException : DomainException
{
    public BusinessRuleViolationException(string ruleCode, string message) : base(message) => RuleCode = ruleCode;

    /// <summary>Stable machine-readable code such as <c>STOCK_INSUFFICIENT</c>.</summary>
    public string RuleCode { get; }
}

/// <summary>The requested record does not exist.</summary>
public sealed class EntityNotFoundException : DomainException
{
    public EntityNotFoundException(string entityName, object key)
        : base($"{entityName} '{key}' was not found.") => EntityName = entityName;

    public string EntityName { get; }
}

/// <summary>The caller is signed in but may not perform this operation.</summary>
public sealed class ForbiddenOperationException : DomainException
{
    public ForbiddenOperationException(string message) : base(message) { }
}

/// <summary>A command failed attribute validation.</summary>
public sealed class CommandValidationException : DomainException
{
    public CommandValidationException(IReadOnlyList<ValidationError> errors)
        : base(errors.Count == 1 ? errors[0].Message : "One or more validation errors occurred.") => Errors = errors;

    public CommandValidationException(string propertyName, string message)
        : this([new ValidationError(propertyName, message)]) { }

    public IReadOnlyList<ValidationError> Errors { get; }
}

/// <summary>
/// Another user changed the same data first (optimistic concurrency, e.g. two clerks selling the
/// last unit). Raised by the persistence layer instead of DbUpdateConcurrencyException so the
/// application never depends on EF Core; mapped to HTTP 409 or a view error.
/// </summary>
public sealed class ConcurrencyConflictException : DomainException
{
    public ConcurrencyConflictException(string message, Exception? innerException = null)
        : base(message) => Cause = innerException;

    public Exception? Cause { get; }
}
