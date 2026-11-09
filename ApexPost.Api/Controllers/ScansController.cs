using ApexPost.Api.Contracts;
using ApexPost.Services.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Controllers;

/// <summary>Scan history and the idempotent bulk upload used by the courier app. Thin: all rules are in <see cref="IShipmentTrackingService"/>.</summary>
[ApiController]
[Route("api/shipments/{trackingNumber:regex(^APX-\\d{{8}}$)}/scans")]
[Tags("Scans")]
public sealed class ScansController(IShipmentTrackingService shipments) : ControllerBase
{
    /// <summary>Uploads a batch of offline scans. Safe to retry.</summary>
    /// <remarks>
    /// Each scan carries the scan id created on the courier's device. A scan id that is already stored is
    /// counted in <c>duplicatesSkipped</c> and not stored again, so a retry after a dropped connection
    /// never creates duplicates. Scans that cannot be stored are listed in <c>rejected</c> with a reason.
    /// </remarks>
    /// <param name="trackingNumber">Tracking number, for example APX-10000001.</param>
    /// <param name="request">The scans to upload (1 to 100).</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <response code="200">The batch was processed; the body says how many scans were accepted, skipped as duplicates and rejected.</response>
    /// <response code="400">The body failed validation (for example a scan in the future, or a Delivered scan without a signature name).</response>
    /// <response code="404">No shipment has this tracking number.</response>
    /// <response code="415">The request is not application/json.</response>
    /// <response code="500">Unexpected error.</response>
    [HttpPost(Name = "UploadScans")]
    [Consumes("application/json")]
    [EndpointSummary("Upload scans (idempotent)")]
    [ProducesResponseType<ScanUploadResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<ScanUploadResponse>> UploadScans(string trackingNumber, ScanUploadRequest request, CancellationToken cancellationToken)
    {
        var result = await shipments.UploadScansAsync(trackingNumber, request.Scans.Select(ApiMapper.ToInput).ToList(), cancellationToken);
        return ApiMapper.ToResponse(result);
    }

    /// <summary>Gets the scan history of a shipment, oldest first.</summary>
    /// <param name="trackingNumber">Tracking number, for example APX-10000001.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <response code="200">The scans (empty when there are none yet).</response>
    /// <response code="404">No shipment has this tracking number.</response>
    /// <response code="500">Unexpected error.</response>
    [HttpGet(Name = "GetScans")]
    [EndpointSummary("Get scan history")]
    [ProducesResponseType<IReadOnlyList<ScanResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<ScanResponse>>> GetScans(string trackingNumber, CancellationToken cancellationToken)
    {
        var scans = await shipments.GetScansAsync(trackingNumber, cancellationToken);
        return scans.Select(ApiMapper.ToResponse).ToList();
    }
}
