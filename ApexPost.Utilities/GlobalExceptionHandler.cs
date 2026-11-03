using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ApexPost.Utilities.Filters;
using ApexPost.Utilities.Middleware;

namespace ApexPost.Utilities;

/// <summary>
/// Last-resort handler for anything that escaped the MVC filters (and for non-MVC endpoints).
/// Writes Problem Details with a safe message plus trace and correlation IDs. The full exception
/// goes to the log only; the response never contains a stack trace or exception message in
/// Production. Browser page requests return <c>false</c> so the framework re-executes the friendly
/// <c>/Home/Error</c> page instead of showing raw JSON.
/// </summary>
public sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var status = DomainExceptionMapper.StatusFor(exception);
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // Cancellation is not a failure: the client went away, so there is nobody to answer.
            LogCancelled(httpContext.Request.Path.Value ?? "/");
            return true;
        }

        LogUnhandled(exception, httpContext.Request.Method, httpContext.Request.Path.Value ?? "/", CorrelationIdMiddleware.Get(httpContext));

        var wantsHtml = httpContext.Request.Headers.Accept.Any(a => a?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
        var isApi = httpContext.Request.Path.StartsWithSegments("/integration", StringComparison.OrdinalIgnoreCase);
        if (wantsHtml && !isApi)
        {
            return false; // falls through to ExceptionHandlingPath (/Home/Error)
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = DomainExceptionMapper.TitleFor(exception),
            Detail = status == StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred. Quote the correlation ID when contacting support."
                : exception.Message,
        };

        if (environment.IsDevelopment())
        {
            problem.Extensions["exception"] = exception.GetType().Name; // type only, still no stack trace
        }

        await ProblemResponses.WriteAsync(httpContext, problem).ConfigureAwait(false);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path} (CorrelationId {CorrelationId})")]
    private partial void LogUnhandled(Exception exception, string method, string path, string correlationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request to {Path} was cancelled by the client")]
    private partial void LogCancelled(string path);
}
