using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using ApexPost.Models.Exceptions;
using ApexPost.Services.Abstractions;

namespace ApexPost.Utilities.Filters;

/// <summary>
/// One mapping from domain exceptions to HTTP status, shared by filters and the exception handler:
/// <list type="bullet">
/// <item>400 Bad Request: invalid input or a broken business rule (the request cannot be
/// processed as sent; the body carries the rule code, e.g. <c>STOCK_INSUFFICIENT</c>).</item>
/// <item>403 / 404: not allowed / not found.</item>
/// <item>409 Conflict: the request clashed with the current state of the data: an optimistic
/// concurrency conflict (rowversion) or a unique-key violation (SQL 2601/2627).</item>
/// <item>500: anything unexpected (no details leak).</item>
/// </list>
/// </summary>
public static class DomainExceptionMapper
{
    public static int StatusFor(Exception exception) => exception switch
    {
        CommandValidationException => StatusCodes.Status400BadRequest,
        BusinessRuleViolationException => StatusCodes.Status400BadRequest,
        ForbiddenOperationException => StatusCodes.Status403Forbidden,
        EntityNotFoundException => StatusCodes.Status404NotFound,
        ConcurrencyConflictException => StatusCodes.Status409Conflict,
        DuplicateKeyException => StatusCodes.Status409Conflict,
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
        DuplicateKeyException => "DUPLICATE_KEY",
        _ => exception.GetType().Name,
    };

    /// <summary>Problem Details title for an exception (400 has two meanings, so the type decides).</summary>
    public static string TitleFor(Exception exception) => exception switch
    {
        BusinessRuleViolationException => "Business rule violated",
        DuplicateKeyException => "Duplicate value",
        _ => TitleFor(StatusFor(exception)),
    };

    public static string TitleFor(int status) => status switch
    {
        400 => "Validation failed",
        403 => "Forbidden",
        404 => "Not found",
        409 => "Conflicting change",
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
