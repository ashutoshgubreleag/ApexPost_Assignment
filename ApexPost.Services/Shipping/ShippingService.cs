using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.Validation;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Caching;
using ApexPost.Services.Common;
using ApexPost.Services.Courier;

namespace ApexPost.Services.Shipping;

public sealed class ShippingService(
    IReferenceDataCache referenceData,
    ITransactionQueries queries,
    ICourierRateService courierRates,
    IShippingPricingStrategy pricing) : IShippingService
{
    public async Task<IReadOnlyList<ZoneDto>> GetZonesAsync(CancellationToken cancellationToken)
    {
        var zones = await referenceData.GetZonesAsync(cancellationToken).ConfigureAwait(false);
        return zones.Values.OrderBy(z => z.ZoneCode, StringComparer.Ordinal).ToList();
    }

    public async Task<ShippingQuote> QuoteAsync(ShippingQuoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ReflectionValidator.EnsureValid(command);

        var zones = await referenceData.GetZonesAsync(cancellationToken).ConfigureAwait(false);
        if (!zones.TryGetValue(command.ZoneCode, out var zone))
        {
            throw new CommandValidationException(nameof(command.ZoneCode), $"Unknown destination zone '{command.ZoneCode}'.");
        }

        var weight = new Weight(command.WeightKg);
        var dimensions = new Dimensions(command.LengthCm, command.WidthCm, command.HeightCm);
        ServiceLevelRules.EnsureAcceptable(command.ShippingType, command.ServiceLevel, weight, dimensions);

        // External call: bounded by the courier service's SemaphoreSlim and cancellable.
        var fuelRate = await courierRates.GetFuelSurchargeRateAsync(zone, cancellationToken).ConfigureAwait(false);

        return pricing.Price(new ShippingPricingContext(command.ShippingType, command.ServiceLevel, zone, weight, dimensions, fuelRate));
    }

    public async Task<ShipmentDto> GetShipmentAsync(int shipmentId, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var shipment = await queries.GetShipmentAsync(shipmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException("Shipment", shipmentId);

        return actor.CanReadBranch(shipment.BranchCode)
            ? shipment
            : throw new ForbiddenOperationException("You can only view shipments from your own branch.");
    }
}
