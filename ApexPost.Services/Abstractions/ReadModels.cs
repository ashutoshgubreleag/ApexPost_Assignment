using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;

namespace ApexPost.Services.Abstractions;

/*
 * Response / view-model DTOs. Every screen and report reads one of these; none of them is an
 * entity, so a view can never lazy-load, change-track or over-post anything. Query services fill
 * them with AsNoTracking projections (Select) or stored procedures.
 */

public sealed record CounterDto(int Number, string Name, bool IsActive);

public sealed record BranchDto(
    int Id,
    BranchCode BranchCode,
    string Name,
    string Region,
    string OpeningHours,
    BranchStatus Status,
    IReadOnlyList<CounterDto> Counters)
{
    public int ActiveCounterCount => Counters.Count(c => c.IsActive);
}

public sealed record StaffDto(int Id, string Name, string Email, StaffRole Role, BranchCode HomeBranchCode, bool IsActive);

public sealed record ProductDto(int Id, Sku Sku, string Name, ProductCategory Category, Money UnitPrice, TaxCategory TaxCategory, bool IsActive);

public sealed record StockDto(BranchCode BranchCode, Sku Sku, string ProductName, int QuantityOnHand, int ReorderThreshold)
{
    public bool IsLow(int defaultThreshold) => QuantityOnHand <= Math.Max(ReorderThreshold, defaultThreshold);
}

public sealed record CatalogueItem(ProductDto Product, int QuantityOnHand, int ReorderThreshold, bool IsLowStock);

public sealed record ZoneDto(string ZoneCode, string Name, bool IsInternational, bool IsRemote, Money LetterBaseRate, Money ParcelBaseRate, Money PerKgRate);

public sealed record TransactionSummaryDto(
    int Id,
    BranchCode BranchCode,
    int CounterNumber,
    int ClerkStaffId,
    TransactionSource Source,
    TransactionStatus Status,
    int ItemCount,
    Money Subtotal,
    Money TaxAmount,
    Money PassThroughAmount,
    Money TotalAmount,
    PaymentMethod? PaymentMethod,
    string? VoidReason,
    int? AuthorisedByStaffId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt)
{
    public bool IsOpen => Status == TransactionStatus.Open;
}

public sealed record TransactionLineDto(
    int LineNumber,
    TransactionLineType LineType,
    string ItemCode,
    string Description,
    int Quantity,
    Money UnitPrice,
    TaxCategory TaxCategory,
    Money TaxAmount,
    Money LineTotal,
    Money PassThroughAmount);

public sealed record PaymentDto(PaymentMethod Method, Money Amount, DateTimeOffset PaidAt);

public sealed record ShipmentDto(
    int Id,
    string TrackingNumber,
    int TransactionId,
    BranchCode BranchCode,
    ShippingType ShippingType,
    ShippingServiceLevel ServiceLevel,
    string ZoneCode,
    Weight Weight,
    Dimensions Dimensions,
    Weight ChargeableWeight,
    Address? Recipient,
    Money Price,
    ShipmentStatus Status,
    DateTimeOffset CreatedAt);

public sealed record MoneyOrderDto(
    int Id,
    string SerialNumber,
    int TransactionId,
    BranchCode BranchCode,
    Money Amount,
    Money Fee,
    string Sender,
    string Recipient,
    int? IdentityVerificationId,
    MoneyOrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? IssuedAt);

public sealed record VerificationDto(
    int Id,
    string Reference,
    int TransactionId,
    BranchCode BranchCode,
    int StaffId,
    string CustomerName,
    DocumentType DocumentType,
    MaskedDocumentNumber MaskedDocumentNumber,
    VerificationResult Result,
    DateTimeOffset VerifiedAt);

/// <summary>Everything the basket screen needs, loaded by one query service call.</summary>
public sealed record TransactionDetails(
    TransactionSummaryDto Transaction,
    IReadOnlyList<TransactionLineDto> Lines,
    IReadOnlyList<PaymentDto> Payments,
    IReadOnlyList<ShipmentDto> Shipments,
    IReadOnlyList<MoneyOrderDto> MoneyOrders,
    IReadOnlyList<VerificationDto> Verifications,
    bool IdentityVerificationRequired,
    bool IdentityVerificationSatisfied)
{
    public Money RetailNet => Lines.Where(l => l.LineType == TransactionLineType.RetailItem).Select(l => l.LineTotal).Total();

    public Money ServicesNet => Lines.Where(l => l.LineType != TransactionLineType.RetailItem).Select(l => l.LineTotal).Total();

    /// <summary>The shipment behind a shipping line (lines reference shipments by tracking number).</summary>
    public ShipmentDto? ShipmentFor(TransactionLineDto line) =>
        line.LineType == TransactionLineType.Shipping ? Shipments.FirstOrDefault(s => s.TrackingNumber == line.ItemCode) : null;

    /// <summary>Why the basket cannot be paid yet (null when it can).</summary>
    public string? PaymentBlocker =>
        Transaction.Status != TransactionStatus.Open ? "Transaction is not open."
        : Lines.Count == 0 ? "Add at least one item or service."
        : IdentityVerificationRequired && !IdentityVerificationSatisfied ? "A passed identity verification is required for the high-value money order."
        : null;
}

public sealed record NotificationDto(NotificationType Type, BranchCode? BranchCode, string Recipient, string Message, string CorrelationId, DateTimeOffset CreatedAt);

public sealed record SyncConflictDto(int Id, string DeviceId, Guid LocalTransactionId, BranchCode BranchCode, SyncConflictType Type, string ItemCode, string Details, SyncConflictStatus Status, DateTimeOffset RaisedAt, int? ServerTransactionId);
