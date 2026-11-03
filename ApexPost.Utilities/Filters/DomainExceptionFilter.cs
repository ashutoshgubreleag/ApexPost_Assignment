using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using ApexPost.Models.Exceptions;
using ApexPost.Utilities.Middleware;

namespace ApexPost.Utilities.Filters;

/// <summary>
/// Turns expected business failures into the right MVC result, so controllers contain no
/// try/catch blocks:
/// <list type="bullet">
/// <item>Not found → 404; forbidden → 403 (Forbid).</item>
/// <item>Business rule / validation failure on a form post → message in TempData and a redirect
/// back to the page (Post/Redirect/Get).</item>
/// <item>API callers → Problem Details with the mapped status and rule code.</item>
/// </list>
/// Unexpected exceptions are left alone and reach the global exception handler (500, no details).
/// A filter, not middleware, because it needs the controller, TempData and IActionResult types.
/// </summary>
public sealed class DomainExceptionFilter(ITempDataDictionaryFactory tempDataFactory) : IExceptionFilter
{
    public const string ErrorTempDataKey = "Error";

    public void OnException(ExceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Exception is not DomainException exception)
        {
            return;
        }

        var http = context.HttpContext;
        var status = DomainExceptionMapper.StatusFor(exception);
        var isApi = DomainExceptionMapper.IsApiRequest(context.ActionDescriptor, http);

        if (isApi)
        {
            var problem = new ProblemDetails
            {
                Status = status,
                Title = DomainExceptionMapper.TitleFor(exception),
                Detail = exception.Message,
            };
            problem.Extensions["code"] = DomainExceptionMapper.ErrorCodeFor(exception);
            problem.Extensions["correlationId"] = CorrelationIdMiddleware.Get(http);
            context.Result = new ObjectResult(problem) { StatusCode = status };
        }
        else
        {
            context.Result = exception switch
            {
                EntityNotFoundException => new NotFoundResult(),
                ForbiddenOperationException => new ForbidResult(),
                _ when HttpMethods.IsGet(http.Request.Method) => new BadRequestObjectResult(exception.Message),
                _ => RedirectBack(context, exception.Message),
            };
        }

        context.ExceptionHandled = true;
    }

    private IActionResult RedirectBack(ExceptionContext context, string message)
    {
        var http = context.HttpContext;
        tempDataFactory.GetTempData(http)[ErrorTempDataKey] = message;

        // Only follow the Referer back to our own site (open-redirect protection).
        var referer = http.Request.Headers.Referer.ToString();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            && string.Equals(uri.Host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return new LocalRedirectResult(uri.PathAndQuery);
        }

        var redirect = context.ActionDescriptor.EndpointMetadata.OfType<RedirectOnInvalidModelAttribute>().FirstOrDefault();
        return redirect is not null
            ? new RedirectToActionResult(redirect.Action, null, new { id = context.RouteData.Values["id"] })
            : new RedirectToActionResult("Index", null, null);
    }
}
