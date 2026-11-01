using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ApexPost.MVC.Models;
using ApexPost.MVC.Security;
using ApexPost.Utilities.Middleware;

namespace ApexPost.MVC.Controllers;

public sealed class HomeController : ApexPostController
{
    /// <summary>Everyone lands on their own branch dashboard.</summary>
    public IActionResult Index() => RedirectToAction("Dashboard", "Branch", new { branchCode = User.BranchCode.Value });

    [AllowAnonymous]
    public IActionResult Privacy() => View();

    /// <summary>Friendly error page for browser requests (re-executed by the exception handler).</summary>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        _ = HttpContext.Features.Get<IExceptionHandlerPathFeature>(); // the exception is logged, never shown
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View(new ErrorViewModel
        {
            StatusCode = 500,
            Title = "Something went wrong",
            Message = "The request could not be completed. Please try again or contact support with the reference below.",
            CorrelationId = CorrelationIdMiddleware.Get(HttpContext),
            TraceId = Activity.Current?.TraceId.ToString(),
        });
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [ActionName("StatusCode")]
    public IActionResult StatusCodePage([FromRoute] int id)
    {
        var code = id is >= 400 and <= 599 ? id : 500;
        Response.StatusCode = code;
        var (title, message) = code switch
        {
            403 => ("Access denied", "Your role or branch does not allow this action."),
            404 => ("Not found", "The page or record you asked for does not exist."),
            429 => ("Slow down", "Too many attempts. Wait a minute and try again."),
            _ => ("Request failed", "The request could not be completed."),
        };

        return View("Error", new ErrorViewModel
        {
            StatusCode = code,
            Title = title,
            Message = message,
            CorrelationId = CorrelationIdMiddleware.Get(HttpContext),
        });
    }
}
