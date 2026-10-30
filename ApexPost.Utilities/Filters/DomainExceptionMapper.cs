using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using ApexPost.Models.Exceptions;

namespace ApexPost.Utilities.Filters;

/// <summary>One mapping from domain exceptions to HTTP status, shared by filters and the exception handler.</summary>
public static class DomainExceptionMapper
{
    public static int StatusFor(Exception exception) => exception switch
    {
        CommandValidationException => StatusCodes.Status400BadRequest,
        ForbiddenOperationException => StatusCodes.Status403Forbidden,
        EntityNotFoundException => StatusCodes.Status404NotFound,
        BusinessRuleViolationException => StatusCodes.Status422UnprocessableEntity,
        ConcurrencyConflictException => StatusCodes.Status409Conflict,
        OperationCanceledException => 499, // client closed request (nginx convention); nobody reads it
        _ => StatusCodes.Status500InternalServerError,
    };

    public static string ErrorCodeFor(Exception exception) => exception switch
    {
        BusinessRuleViolationException rule => rule.RuleCode,
        CommandValidationException => "VALIDATION_FAILED",
        ForbiddenOperationException => "FORBIDDEN",
        EntityNotFoundException => "NOT_FOUND",
        ConcurrencyConflictException => "CONCURRENCY_CONFLICT",
        _ => exception.GetType().Name,
    };

    public static string TitleFor(int status) => status switch
    {
        400 => "Validation failed",
        403 => "Forbidden",
        404 => "Not found",
        409 => "Conflicting change",
        422 => "Business rule violated",
        _ => "An unexpected error occurred",
    };

    /// <summary>True for API endpoints (JSON in, JSON out) as opposed to Razor pages.</summary>
    public static bool IsApiRequest(ActionDescriptor action, HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(http);
        return action.EndpointMetadata.Any(m => m is ApiControllerAttribute or JsonApiAttribute)
            || http.Request.Headers.Accept.Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
    }
}
