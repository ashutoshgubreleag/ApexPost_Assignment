using System.Diagnostics;
using ApexPost.Utilities.Middleware;
using Microsoft.AspNetCore.Http;

namespace ApexPost.Api.Infrastructure;

/// <summary>Adds the trace and correlation ids to every ProblemDetails the framework writes (status-code pages, 415, 404 ...).</summary>
public static class ProblemDetailsCustomizer
{
    /// <summary>Callback for <c>ProblemDetailsOptions.CustomizeProblemDetails</c>.</summary>
    /// <param name="context">The problem details being written.</param>
    public static void Apply(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions["correlationId"] = CorrelationIdMiddleware.Get(context.HttpContext);
    }
}
