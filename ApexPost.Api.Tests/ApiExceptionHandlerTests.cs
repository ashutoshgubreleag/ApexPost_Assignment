using ApexPost.Api.Infrastructure;
using ApexPost.Models.Exceptions;
using ApexPost.Models.Validation;
using ApexPost.Services.Abstractions;

namespace ApexPost.Api.Tests;

/// <summary>The exception-to-ProblemDetails mapping used by the global handler.</summary>
public sealed class ApiExceptionHandlerTests
{
    [Fact]
    public void ConflictException_Is409WithItsErrorCode()
    {
        var problem = ApiExceptionHandler.Build(new ConflictException("INVALID_STATUS_TRANSITION", "Cannot move."));

        Assert.Equal(409, problem.Status);
        Assert.Equal("INVALID_STATUS_TRANSITION", problem.Extensions["errorCode"]);
        Assert.Equal("Cannot move.", problem.Detail);
    }

    [Fact]
    public void EntityNotFound_Is404() =>
        Assert.Equal(404, ApiExceptionHandler.Build(new EntityNotFoundException("Shipment", "APX-10000001")).Status);

    [Fact]
    public void DuplicateKeyAndConcurrencyConflicts_Are409()
    {
        Assert.Equal(409, ApiExceptionHandler.Build(new DuplicateKeyException("UQ_ShipmentScans_ScanId")).Status);
        Assert.Equal(409, ApiExceptionHandler.Build(new ConcurrencyConflictException("changed")).Status);
    }

    [Fact]
    public void CommandValidation_Is400AndListsTheFields()
    {
        var problem = ApiExceptionHandler.Build(new CommandValidationException([new ValidationError("City", "City is required."), new ValidationError("City", "Too short."), new ValidationError("WeightKg", "Out of range.")]));

        Assert.Equal(400, problem.Status);
        var errors = Assert.IsType<Dictionary<string, string[]>>(problem.Extensions["errors"]);
        Assert.Equal(2, errors["City"].Length);
        Assert.Single(errors["WeightKg"]);
    }

    [Fact]
    public void UnexpectedException_Is500AndNeverLeaksItsMessage()
    {
        var problem = ApiExceptionHandler.Build(new InvalidOperationException("Server=prod-db;Password=hunter2"));

        Assert.Equal(500, problem.Status);
        Assert.DoesNotContain("hunter2", problem.Detail, StringComparison.Ordinal);
        Assert.False(problem.Extensions.ContainsKey("errorCode"));
    }
}
