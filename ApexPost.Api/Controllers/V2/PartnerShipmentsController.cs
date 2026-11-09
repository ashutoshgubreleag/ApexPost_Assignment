using ApexPost.Api.Contracts;
using ApexPost.Api.Contracts.V2;
using ApexPost.Api.Security;
using ApexPost.Services.Abstractions;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Controllers.V2;

/// <summary>
/// Read-only tracking lookup for partner systems (server to server). It does not use a JWT: partners present an
/// <c>X-Api-Key</c> header, validated by <see cref="PartnerApiKeyAttribute"/> before the action runs. Thin: the lookup is <see cref="IShipmentTrackingService"/>.
/// </summary>
[ApiController]
[ApiVersion("2.0")]
[AllowAnonymous]
[PartnerApiKey]
[Route("api/v{version:apiVersion}/partner/shipments")]
[Tags("Partner")]
public sealed class PartnerShipmentsController(IShipmentTrackingService shipments) : ControllerBase
{
    /// <summary>Gets one shipment by tracking number (partner, read-only).</summary>
    /// <param name="trackingNumber">Tracking number, for example APX-10000001.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <response code="200">The shipment (same shape as the v2 shipment response).</response>
    /// <response code="401">The <c>X-Api-Key</c> header is missing or not a valid partner key.</response>
    /// <response code="404">No shipment has this tracking number.</response>
    /// <response code="500">Unexpected error, or no partner key is configured on the server.</response>
    [HttpGet("{trackingNumber:regex(^APX-\\d{{8}}$)}", Name = "GetPartnerShipment")]
    [EndpointSummary("Get a shipment (partner)")]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<ShipmentResponse>> GetShipment(string trackingNumber, CancellationToken cancellationToken) =>
        ApiMapper.ToV2(await shipments.GetAsync(trackingNumber, cancellationToken));
}
