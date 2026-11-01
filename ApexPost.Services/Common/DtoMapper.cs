using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;

namespace ApexPost.Services.Common;

/// <summary>
/// Explicit domain → response DTO mapping for command results (what a write use case returns).
/// Read screens never come through here: query services project straight to DTOs in SQL.
/// </summary>
public static class DtoMapper
{
    public static BranchDto ToDto(this Models.Domain.Branch branch) => new(
        branch.Id,
        branch.Code,
        branch.Name,
        branch.Region,
        branch.OpeningHours,
        branch.Status,
        branch.Counters.OrderBy(c => c.Number).Select(c => new CounterDto(c.Number, c.Name, c.IsActive)).ToList());

    public static StaffDto ToDto(this StaffMember staff) =>
        new(staff.Id, staff.Name, staff.Email, staff.Role, staff.HomeBranchCode, staff.IsActive);

    public static ProductDto ToDto(this Product product) =>
        new(product.Id, product.Sku, product.Name, product.Category, product.UnitPrice, product.TaxCategory, product.IsActive);

    public static TransactionSummaryDto ToSummary(this CounterTransaction tx) => new(
        tx.Id,
        tx.BranchCode,
        tx.CounterNumber,
        tx.ClerkStaffId,
        tx.Source,
        tx.Status,
        tx.ItemCount,
        tx.Subtotal,
        tx.TaxAmount,
        tx.PassThroughAmount,
        tx.TotalAmount,
        tx.PaymentMethod,
        tx.VoidReason,
        tx.AuthorisedByStaffId,
        tx.CreatedAt,
        tx.ClosedAt);

    public static ShipmentDto ToDto(this Shipment s) => new(
        s.Id, s.TrackingNumber, s.TransactionId, s.BranchCode, s.ShippingType, s.ServiceLevel, s.ZoneCode,
        s.Weight, s.Dimensions, s.ChargeableWeight, s.Recipient, s.Price, s.Status, s.CreatedAt);

    public static MoneyOrderDto ToDto(this MoneyOrder m) => new(
        m.Id, m.SerialNumber, m.TransactionId, m.BranchCode, m.Amount, m.Fee, m.Sender, m.Recipient,
        m.IdentityVerificationId, m.Status, m.CreatedAt, m.IssuedAt);

    public static VerificationDto ToDto(this IdentityVerification v) => new(
        v.Id, v.Reference, v.TransactionId, v.BranchCode, v.StaffId, v.CustomerName, v.DocumentType,
        v.MaskedDocumentNumber, v.Result, v.VerifiedAt);
}
