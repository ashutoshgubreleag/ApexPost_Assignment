using ApexPost.Models.Exceptions;
using ApexPost.Repositories.Persistence;
using ApexPost.Services.Abstractions;
using ApexPost.Utilities.Filters;
using Microsoft.AspNetCore.Http;

namespace ApexPost.Tests.Unit;

/// <summary>
/// Week 3 review fixes: a broken business rule is a 400 (with its rule code), and a unique-key
/// violation is a 409 like any other conflicting change, never a 500.
/// </summary>
public sealed class ExceptionMappingTests
{
    [Fact]
    public void Business_rule_violation_is_400_with_its_rule_code()
    {
        var ex = new BusinessRuleViolationException("STOCK_INSUFFICIENT", "Only 2 left.");

        Assert.Equal(StatusCodes.Status400BadRequest, DomainExceptionMapper.StatusFor(ex));
        Assert.Equal("STOCK_INSUFFICIENT", DomainExceptionMapper.ErrorCodeFor(ex));
        Assert.Equal("Business rule violated", DomainExceptionMapper.TitleFor(ex));
    }

    [Fact]
    public void Validation_failure_is_400()
    {
        var ex = new CommandValidationException("Email", "Email is required.");

        Assert.Equal(StatusCodes.Status400BadRequest, DomainExceptionMapper.StatusFor(ex));
        Assert.Equal("Validation failed", DomainExceptionMapper.TitleFor(ex));
    }

    [Fact]
    public void Duplicate_key_is_409_and_names_the_constraint_not_the_value()
    {
        var ex = new DuplicateKeyException("UQ_Staff_Email");

        Assert.IsAssignableFrom<DomainException>(ex);
        Assert.Equal(StatusCodes.Status409Conflict, DomainExceptionMapper.StatusFor(ex));
        Assert.Equal("DUPLICATE_KEY", DomainExceptionMapper.ErrorCodeFor(ex));
        Assert.Equal("Duplicate value", DomainExceptionMapper.TitleFor(ex));
        Assert.Contains("UQ_Staff_Email", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Concurrency_conflict_is_409()
    {
        Assert.Equal(StatusCodes.Status409Conflict, DomainExceptionMapper.StatusFor(new ConcurrencyConflictException("changed")));
    }

    [Fact]
    public void Unexpected_exceptions_stay_500()
    {
        Assert.Equal(StatusCodes.Status500InternalServerError, DomainExceptionMapper.StatusFor(new InvalidOperationException("boom")));
    }

    [Theory]
    [InlineData("Violation of UNIQUE KEY constraint 'UQ_Shipments_TrackingNumber'. Cannot insert duplicate key in object 'dbo.Shipments'. The duplicate key value is (APX260901000025).", "UQ_Shipments_TrackingNumber")]
    [InlineData("Cannot insert duplicate key row in object 'dbo.Staff' with unique index 'UQ_Staff_Email'. The duplicate key value is (someone@apexpost.local).", "UQ_Staff_Email")]
    [InlineData("Violation of PRIMARY KEY constraint 'PK_TransactionLines'. Cannot insert duplicate key in object 'dbo.TransactionLines'.", "PK_TransactionLines")]
    [InlineData("something unexpected", "unique key")]
    public void Constraint_name_is_read_from_the_sql_error_and_the_value_is_dropped(string sqlMessage, string expected)
    {
        var name = SqlErrorText.ConstraintName(sqlMessage);
        var ex = new DuplicateKeyException(name);

        Assert.Equal(expected, name);
        Assert.DoesNotContain("someone@apexpost.local", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("APX260901000025", ex.Message, StringComparison.Ordinal);
    }
}
