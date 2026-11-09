using ApexPost.Models.Exceptions;
using ApexPost.Utilities.Filters;
using ApexPost.Utilities.Middleware;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Infrastructure;

/// <summary>
/// The global exception handler (<see cref="IExceptionHandler"/>). Expected business failures
/// (<see cref="DomainException"/>) become 400/404/409 ProblemDetails with a stable error code;
/// everything else becomes a generic 500. Stack traces and exception messages of unexpected errors
/// are only ever written in Development.
/// </summary>
public sealed class ApiExceptionHandler(IHostEnvironment environment, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499; // client went away: nobody reads the body
            return true;
        }

        var problem = Build(exception);
        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            if (environment.IsDevelopment())
            {
                problem.Detail = exception.ToString();
            }
        }
        else
        {
            logger.LogInformation("{ExceptionType} handled as {Status} for {Method} {Path}: {Message}",
                exception.GetType().Name, problem.Status, httpContext.Request.Method, httpContext.Request.Path, exception.Message);
        }

        await ProblemResponses.WriteAsync(httpContext, problem).ConfigureAwait(false);
        return true;
    }

    /// <summary>Maps an exception to ProblemDetails. Public so unit tests can prove the mapping without a host.</summary>
    public static ProblemDetails Build(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var status = exception is ConflictException ? StatusCodes.Status409Conflict : DomainExceptionMapper.StatusFor(exception);
        var isExpected = exception is DomainException;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = exception is ConflictException ? "Conflict" : DomainExceptionMapper.TitleFor(exception),
            // Domain messages are written for callers; messages of unexpected exceptions never leave the server.
            Detail = isExpected ? exception.Message : "An unexpected error occurred. Quote the traceId when contacting support.",
        };

        if (isExpected)
        {
            problem.Extensions["errorCode"] = exception is ConflictException conflict ? conflict.ErrorCode : DomainExceptionMapper.ErrorCodeFor(exception);
        }

        if (exception is CommandValidationException validation)
        {
            problem.Extensions["errors"] = validation.Errors
                .GroupBy(e => e.PropertyName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray(), StringComparer.Ordinal);
        }

        return problem;
    }
}
