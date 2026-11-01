using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Common;

namespace ApexPost.Services.Shipping;

public interface IShippingService
{
    Task<IReadOnlyList<ZoneDto>> GetZonesAsync(CancellationToken cancellationToken);

    /// <summary>Validates the parcel against the service level and prices it (no data is written).</summary>
    Task<ShippingQuote> QuoteAsync(ShippingQuoteCommand command, CancellationToken cancellationToken);

    Task<ShipmentDto> GetShipmentAsync(int shipmentId, StaffContext actor, CancellationToken cancellationToken);
}
