using System.Security.Claims;
using ApexPost.Api.Contracts;
using ApexPost.Utilities.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ApexPost.Api.Security;

/// <summary>
/// Action filter for the scan upload: a courier may only upload scans recorded under their own <c>courierId</c> claim.
/// A scan that names a different courier, or a token without a <c>courierId</c> claim, is answered with 403 ProblemDetails
/// before the service runs. It runs after model validation, so a malformed body is still a 400.
/// </summary>
internal sealed class ScanOwnershipFilter : IAsyncActionFilter
{
    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var request = context.ActionArguments.Values.OfType<ScanUploadRequest>().FirstOrDefault();
        var ownId = context.HttpContext.User.FindFirstValue(ApiRoles.CourierIdClaim);
        if (request is not null && (string.IsNullOrEmpty(ownId) || request.Scans.Any(s => s.CourierId is not null && !string.Equals(s.CourierId, ownId, StringComparison.Ordinal))))
        {
            await ProblemResponses.WriteAsync(context.HttpContext, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = "A courier may only upload scans recorded under their own courierId.",
                Extensions = { ["errorCode"] = "COURIER_MISMATCH" },
            }).ConfigureAwait(false);
            context.Result = new EmptyResult();
            return;
        }

        await next().ConfigureAwait(false);
    }
}
