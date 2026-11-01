using ApexPost.Models.Domain;
using ApexPost.Models.Validation;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Shipping;

namespace ApexPost.Services.Transactions;

public sealed record AddRetailItemCommand
{
    [NotBlank, Matches("^[A-Za-z]{3}-[0-9]{3}$", "a SKU such as PKG-001")]
    public string Sku { get; init; } = string.Empty;

    [InRange(1, 100)]
    public int Quantity { get; init; } = 1;
}

public sealed record AddShipmentCommand(ShippingQuoteCommand Quote, RecipientCommand Recipient);

public sealed record VoidOrRefundCommand
{
    [NotBlank, MaxLengthRule(200), Matches("^.{5,}$", "at least 5 characters")]
    public string Reason { get; init; } = string.Empty;
}
