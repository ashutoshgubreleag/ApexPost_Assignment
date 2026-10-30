using System.ComponentModel.DataAnnotations;
using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;

namespace ApexPost.MVC.Models;

// Form models carry DataAnnotations for ModelState (consumed by the ValidationFilter). The service
// layer validates its commands again with domain rule attributes, so a form can never bypass a rule.

public sealed class BranchFormViewModel
{
    [Required, RegularExpression("^BR-[0-9]{3}$", ErrorMessage = "Branch code must look like BR-007.")]
    public string BranchCode { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(40)]
    public string Region { get; set; } = string.Empty;

    [Required, StringLength(120), Display(Name = "Opening hours")]
    public string OpeningHours { get; set; } = "Mon-Fri 09:00-17:00";

    [Range(1, 20), Display(Name = "Counter terminals")]
    public int CounterCount { get; set; } = 2;

    public bool IsEdit { get; set; }
}

public sealed class StaffFormViewModel
{
    public int Id { get; set; }

    [Required, StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(120)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public StaffRole Role { get; set; } = StaffRole.CounterClerk;

    [Required, Display(Name = "Home branch")]
    public string HomeBranchCode { get; set; } = string.Empty;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [DataType(DataType.Password), StringLength(128, MinimumLength = 10), Display(Name = "Password")]
    public string? Password { get; set; }
}

public sealed class StockAdjustmentViewModel
{
    [Required]
    public string BranchCode { get; set; } = string.Empty;

    [Required, RegularExpression("^[A-Za-z]{3}-[0-9]{3}$")]
    public string Sku { get; set; } = string.Empty;

    [Range(-1000, 1000), Display(Name = "Change")]
    public int QuantityChange { get; set; }

    [Required, StringLength(200, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class OpenTransactionViewModel
{
    [Range(1, 20), Display(Name = "Counter terminal")]
    public int CounterTerminalId { get; set; } = 1;

    public IReadOnlyList<CounterDto> Counters { get; set; } = [];
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
}

public sealed class AddItemViewModel
{
    [Required, RegularExpression("^[A-Za-z]{3}-[0-9]{3}$", ErrorMessage = "Choose a product.")]
    public string Sku { get; set; } = string.Empty;

    [Range(1, 100)]
    public int Quantity { get; set; } = 1;
}

public sealed class PaymentViewModel
{
    [Required]
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Card;
}

public sealed class ReasonViewModel
{
    [Required(ErrorMessage = "A reason is required."), StringLength(200, MinimumLength = 5, ErrorMessage = "Give a reason of at least 5 characters.")]
    public string Reason { get; set; } = string.Empty;
}

public sealed class ShipmentFormViewModel
{
    public int TransactionId { get; set; }

    [Required, Display(Name = "Type")]
    public ShippingType ShippingType { get; set; } = ShippingType.Parcel;

    [Required, Display(Name = "Service level")]
    public ShippingServiceLevel ServiceLevel { get; set; } = ShippingServiceLevel.Standard;

    [Required, Display(Name = "Destination zone")]
    public string ZoneCode { get; set; } = "Z01";

    [Range(0.001, 100), Display(Name = "Weight (kg)")]
    public decimal WeightKg { get; set; } = 1m;

    [Range(0.1, 300), Display(Name = "Length (cm)")]
    public decimal LengthCm { get; set; } = 30m;

    [Range(0.1, 300), Display(Name = "Width (cm)")]
    public decimal WidthCm { get; set; } = 20m;

    [Range(0.1, 300), Display(Name = "Height (cm)")]
    public decimal HeightCm { get; set; } = 10m;

    [Required, StringLength(80), Display(Name = "Recipient")]
    public string RecipientName { get; set; } = string.Empty;

    [Required, StringLength(120), Display(Name = "Address line")]
    public string Line1 { get; set; } = string.Empty;

    [Required, StringLength(60)]
    public string City { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string State { get; set; } = string.Empty;

    [Required, StringLength(12)]
    public string Postcode { get; set; } = string.Empty;
}

public sealed class QuoteFormViewModel
{
    [Required] public ShippingType ShippingType { get; set; } = ShippingType.Parcel;
    [Required] public ShippingServiceLevel ServiceLevel { get; set; } = ShippingServiceLevel.Standard;
    [Required] public string ZoneCode { get; set; } = "Z01";
    [Range(0.001, 100)] public decimal WeightKg { get; set; } = 1m;
    [Range(0.1, 300)] public decimal LengthCm { get; set; } = 30m;
    [Range(0.1, 300)] public decimal WidthCm { get; set; } = 20m;
    [Range(0.1, 300)] public decimal HeightCm { get; set; } = 10m;
}

public sealed class MoneyOrderFormViewModel
{
    public int TransactionId { get; set; }

    [Range(1, 10000, ErrorMessage = "Amount must be between $1 and $10,000.")]
    public decimal Amount { get; set; } = 100m;

    [Required, StringLength(80)]
    public string Sender { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Recipient { get; set; } = string.Empty;

    public decimal IdentityThreshold { get; set; }
}

public sealed class IdentityVerificationFormViewModel
{
    public int TransactionId { get; set; }

    [Required, StringLength(80), Display(Name = "Customer name")]
    public string CustomerName { get; set; } = string.Empty;

    [Required, Display(Name = "Document type")]
    public DocumentType DocumentType { get; set; } = DocumentType.DriverLicence;

    /// <summary>Typed by the clerk, masked by the service, never stored or audited in full.</summary>
    [Required, RegularExpression("^[A-Za-z0-9 -]{5,40}$", ErrorMessage = "Enter 5-40 letters or digits."), Display(Name = "Document number")]
    public string DocumentNumber { get; set; } = string.Empty;

    [Required, Display(Name = "Check result")]
    public VerificationResult Result { get; set; } = VerificationResult.Passed;
}

/// <summary>Request DTO for the admin price revision form (validated before the command is built).</summary>
public sealed class RevisePricesViewModel
{
    [Required]
    public ProductCategory Category { get; set; }

    [Range(-50, 50)]
    public decimal PercentChange { get; set; }

    [Required, StringLength(200, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}
