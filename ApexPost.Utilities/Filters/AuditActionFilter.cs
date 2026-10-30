using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using ApexPost.Utilities.Audit;
using ApexPost.Services.Abstractions;
using ApexPost.Utilities.Middleware;

namespace ApexPost.Utilities.Filters;

/// <summary>
/// Marks an action as a business action that must be audited and names it
/// (<c>[Audited("Transaction.Void", "reason")]</c>). <see cref="Fields"/> is a whitelist of
/// input fields worth recording; nothing else from the request body is ever written.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AuditedAttribute(string action, params string[] fields) : Attribute
{
    public string Action { get; } = action;
    public IReadOnlyList<string> Fields { get; } = fields;
}

/// <summary>
/// Records who did what for every <see cref="AuditedAttribute"/> action: staff, role, branch,
/// controller/action, route values, whitelisted fields, resulting status, duration and correlation ID.
///
/// <para>Why a filter and not middleware: it needs MVC context that middleware does not have:
/// the resolved action and its attribute, bound action arguments (so it can read <c>Sku</c> or
/// <c>Reason</c> without re-reading the body), and the <see cref="IActionResult"/> or exception the
/// action produced. Middleware sees only a byte stream and a final status code.</para>
///
/// <para>Registered globally through <c>Filters.AddService</c>, so it is resolved from DI: it is a
/// singleton that depends only on singletons (the audit store and logger).</para>
/// </summary>
public sealed partial class AuditActionFilter(IAuditStore store, TimeProvider clock, ILogger<AuditActionFilter> logger) : IAsyncActionFilter
{
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> PropertyCache = new();

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var audited = context.ActionDescriptor.EndpointMetadata.OfType<AuditedAttribute>().FirstOrDefault();
        if (audited is null)
        {
            await next().ConfigureAwait(false);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var fields = CaptureFields(context.ActionArguments, audited.Fields); // capture before the action mutates anything
        var executed = await next().ConfigureAwait(false);
        var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        var (status, error) = Outcome(executed, context.HttpContext);
        var user = context.HttpContext.User;
        var entry = new AuditEntry
        {
            TimestampUtc = clock.GetUtcNow(),
            Action = audited.Action,
            StaffId = user.FindFirst("StaffId")?.Value,
            Role = user.FindFirst("Role")?.Value,
            BranchCode = user.FindFirst("BranchCode")?.Value,
            Controller = context.RouteData.Values["controller"]?.ToString() ?? string.Empty,
            ControllerAction = context.RouteData.Values["action"]?.ToString() ?? string.Empty,
            HttpMethod = context.HttpContext.Request.Method,
            Path = context.HttpContext.Request.Path.Value ?? string.Empty,
            RouteValues = context.RouteData.Values
                .Where(kv => kv.Key is not ("controller" or "action") && kv.Value is not null)
                .ToDictionary(kv => kv.Key, kv => AuditRedactor.Redact(kv.Key, Convert.ToString(kv.Value, CultureInfo.InvariantCulture))),
            Fields = fields,
            StatusCode = status,
            Outcome = status switch { < 400 => "Succeeded", < 500 => "Rejected", _ => "Failed" },
            Error = error,
            DurationMs = elapsed,
            CorrelationId = CorrelationIdMiddleware.Get(context.HttpContext),
        };

        try
        {
            // CancellationToken.None on purpose: if the client disconnects after the sale went
            // through, the audit record must still be written.
            await store.AppendAsync(entry, CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            LogAuditWriteFailed(ex, entry.Action, entry.CorrelationId);
        }
    }

    private static (int Status, string? Error) Outcome(ActionExecutedContext executed, HttpContext http)
    {
        if (executed.Exception is { } exception && !executed.ExceptionHandled)
        {
            return (DomainExceptionMapper.StatusFor(exception), DomainExceptionMapper.ErrorCodeFor(exception));
        }

        var status = executed.Result switch
        {
            IStatusCodeActionResult { StatusCode: { } code } => code,
            RedirectToActionResult or RedirectResult or LocalRedirectResult or RedirectToRouteResult => StatusCodes.Status302Found,
            ForbidResult => StatusCodes.Status403Forbidden,
            ChallengeResult => StatusCodes.Status401Unauthorized,
            ViewResult or PartialViewResult or JsonResult or ContentResult => StatusCodes.Status200OK,
            _ => http.Response.StatusCode,
        };

        // Invalid model state redisplays the form (200) but the operation did not happen.
        var error = executed.Controller is Controller { ModelState.IsValid: false } ? "VALIDATION_FAILED" : null;
        return (error is null ? status : StatusCodes.Status400BadRequest, error);
    }

    private static Dictionary<string, string> CaptureFields(IDictionary<string, object?> arguments, IReadOnlyList<string> whitelist)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (whitelist.Count == 0)
        {
            return fields;
        }

        foreach (var name in whitelist)
        {
            // Either a simple action parameter (reason, quantity) or a property of a bound model.
            if (arguments.TryGetValue(name, out var direct) && direct is not null && IsSimple(direct.GetType()))
            {
                fields[name] = AuditRedactor.Redact(name, Convert.ToString(direct, CultureInfo.InvariantCulture));
                continue;
            }

            foreach (var argument in arguments.Values.Where(a => a is not null && !IsSimple(a.GetType())))
            {
                var property = PropertyCache.GetOrAdd((argument!.GetType(), name), static key =>
                    key.Type.GetProperty(key.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
                if (property?.GetValue(argument) is { } value)
                {
                    fields[name] = AuditRedactor.Redact(name, Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                }
            }
        }

        return fields;
    }

    private static bool IsSimple(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateOnly) || type == typeof(Guid);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Failed to write audit entry {Action} (CorrelationId {CorrelationId})")]
    private partial void LogAuditWriteFailed(Exception exception, string action, string correlationId);
}
