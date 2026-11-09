using ApexPost.Api.Contracts;
using ApexPost.Api.Contracts.V1;
using Asp.Versioning;
using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Tracking;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Controllers.V1;

/// <summary>
/// Shipments, API version 1 (DEPRECATED: flat recipient fields, numeric status, no latest scan). Thin by design: bind the input, call <see cref="IShipmentTrackingService"/>, return an
/// HTTP result. Every business rule (duplicates, status transitions, delete rule, paging limits) lives in the service.
/// </summary>
[ApiController]
[ApiVersion("1.0", Deprecated = true)]
[Route("api/v{version:apiVersion}/shipments")]
[Tags("Shipments")]
public sealed class ShipmentsV1Controller(IShipmentTrackingService shipments) : ControllerBase
{
    private const string TrackingNumberRoute = "{trackingNumber:regex(^APX-\\d{{8}}$)}";

    /// <summary>Lists shipments, newest first, with optional filters.</summary>
    /// <param name="page">Page number, starting at 1.</param>
    /// <param name="pageSize">Items per page. Default 20, maximum 50 (larger values are reduced to 50).</param>
    /// <param name="status">Only shipments in this status.</param>
    /// <param name="city">Only shipments delivered to this city (case-insensitive).</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <response code="200">One page of shipments.</response>
    /// <response code="400">A query value could not be understood (for example an unknown status).</response>
    /// <response code="500">Unexpected error.</response>
    [HttpGet(Name = "ListShipmentsV1")]
    [EndpointSummary("List shipments")]
    [EndpointDescription("Paged and filtered list of shipments. pageSize defaults to 20 and is capped at 50.")]
    [ProducesResponseType<ShipmentPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<ShipmentPageResponse>> ListShipments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = TrackingLimits.DefaultPageSize,
        [FromQuery] TrackingStatus? status = null,
        [FromQuery] string? city = null,
        CancellationToken cancellationToken = default)
    {
        var result = await shipments.ListAsync(new TrackingListQuery(page, pageSize, status, city), cancellationToken);
        return ApiMapper.ToV1(result);
    }

    /// <summary>Gets one shipment.</summary>
    /// <param name="trackingNumber">Tracking number, for example APX-10000001.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <response code="200">The shipment.</response>
    /// <response code="404">No shipment has this tracking number.</response>
    /// <response code="500">Unexpected error.</response>
    [HttpGet(TrackingNumberRoute, Name = "GetShipmentV1")]
    [EndpointSummary("Get a shipment")]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<ShipmentResponse>> GetShipment(string trackingNumber, CancellationToken cancellationToken) =>
        ApiMapper.ToV1(await shipments.GetAsync(trackingNumber, cancellationToken));

    /// <summary>Creates a shipment in status Created.</summary>
    /// <param name="request">The shipment to create.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <response code="201">Created. The Location header points at the new shipment.</response>
    /// <response code="400">The body failed validation (ValidationProblemDetails lists every field).</response>
    /// <response code="409">A shipment with this tracking number already exists.</response>
    /// <response code="415">The request is not application/json.</response>
    /// <response code="500">Unexpected error.</response>
    [HttpPost(Name = "CreateShipmentV1")]
    [Consumes("application/json")]
    [EndpointSummary("Create a shipment")]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<ShipmentResponse>> CreateShipment(CreateShipmentRequest request, CancellationToken cancellationToken)
    {
        var created = await shipments.CreateAsync(new CreateTrackedShipmentCommand(request.TrackingNumber, ApiMapper.ToDetails(request)), cancellationToken);
        return CreatedAtRoute("GetShipmentV1", new { version = "1", trackingNumber = created.TrackingNumber }, ApiMapper.ToV1(created));
    }

    /// <summary>Replaces the editable details of a shipment.</summary>
    /// <param name="trackingNumber">Tracking number, for example APX-10000001.</param>
    /// <param name="request">The new details.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <response code="200">The updated shipment.</response>
    /// <response code="400">The body failed validation.</response>
    /// <response code="404">No shipment has this tracking number.</response>
    /// <response code="409">The shipment has already been picked up and can no longer be edited.</response>
    /// <response code="415">The request is not application/json.</response>
    /// <response code="500">Unexpected error.</response>
    [HttpPut(TrackingNumberRoute, Name = "UpdateShipmentV1")]
    [Consumes("application/json")]
    [EndpointSummary("Replace shipment details")]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<ShipmentResponse>> UpdateShipment(string trackingNumber, UpdateShipmentRequest request, CancellationToken cancellationToken) =>
        ApiMapper.ToV1(await shipments.UpdateAsync(trackingNumber, ApiMapper.ToDetails(request), cancellationToken));

    /// <summary>Moves a shipment to its next status.</summary>
    /// <param name="trackingNumber">Tracking number, for example APX-10000001.</param>
    /// <param name="request">The new status.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <response code="200">The shipment with its new status.</response>
    /// <response code="400">The body failed validation.</response>
    /// <response code="404">No shipment has this tracking number.</response>
    /// <response code="409">The transition is not allowed (Created, PickedUp, InTransit, OutForDelivery, then Delivered or Failed).</response>
    /// <response code="415">The request is not application/json.</response>
    /// <response code="500">Unexpected error.</response>
    [HttpPatch($"{TrackingNumberRoute}/status", Name = "UpdateShipmentStatusV1")]
    [Consumes("application/json")]
    [EndpointSummary("Change shipment status")]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<ShipmentResponse>> UpdateShipmentStatus(string trackingNumber, UpdateStatusRequest request, CancellationToken cancellationToken) =>
        ApiMapper.ToV1(await shipments.ChangeStatusAsync(trackingNumber, request.Status!.Value, cancellationToken));

    /// <summary>Deletes a shipment. Allowed only while its status is Created.</summary>
    /// <param name="trackingNumber">Tracking number, for example APX-10000001.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <response code="204">Deleted.</response>
    /// <response code="404">No shipment has this tracking number.</response>
    /// <response code="409">The shipment is no longer in status Created.</response>
    /// <response code="500">Unexpected error.</response>
    [HttpDelete(TrackingNumberRoute, Name = "DeleteShipmentV1")]
    [EndpointSummary("Delete a shipment")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> DeleteShipment(string trackingNumber, CancellationToken cancellationToken)
    {
        await shipments.DeleteAsync(trackingNumber, cancellationToken);
        return NoContent();
    }
}
