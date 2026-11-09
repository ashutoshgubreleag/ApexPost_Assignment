using System.ComponentModel.DataAnnotations;
using ApexPost.Models.Domain;
using ApexPost.Services.Tracking;

namespace ApexPost.Api.Contracts;

/// <summary>Body of <c>POST /api/shipments</c>.</summary>
public sealed class CreateShipmentRequest
{
    /// <summary>Tracking number chosen by the caller, in the form <c>APX-12345678</c>.</summary>
    /// <example>APX-10000001</example>
    [Required]
    [RegularExpression(TrackingLimits.TrackingNumberPattern, ErrorMessage = "Tracking number must look like APX-12345678.")]
    public string TrackingNumber { get; set; } = string.Empty;

    /// <summary>Name of the person receiving the parcel.</summary>
    /// <example>Ada Lovelace</example>
    [Required]
    [StringLength(TrackedShipment.MaxText)]
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>Recipient phone number (digits, optional leading +, spaces, dashes and brackets).</summary>
    /// <example>+61 400 000 111</example>
    [Required]
    [RegularExpression(TrackingLimits.PhonePattern, ErrorMessage = "Phone number is not in a valid format.")]
    public string RecipientPhone { get; set; } = string.Empty;

    /// <summary>Delivery street address.</summary>
    /// <example>12 Example Street</example>
    [Required]
    [StringLength(TrackedShipment.MaxText)]
    public string Address { get; set; } = string.Empty;

    /// <summary>Delivery city.</summary>
    /// <example>Melbourne</example>
    [Required]
    [StringLength(100)]
    public string City { get; set; } = string.Empty;

    /// <summary>Parcel weight in kilograms (0.1 to 70).</summary>
    /// <example>2.5</example>
    [Range(0.1, 70.0, ErrorMessage = "Weight must be between 0.1 and 70 kg.")]
    public decimal WeightKg { get; set; }
}

/// <summary>Body of <c>PUT /api/shipments/{trackingNumber}</c>: replaces every editable detail (the tracking number cannot change).</summary>
public sealed class UpdateShipmentRequest
{
    /// <inheritdoc cref="CreateShipmentRequest.RecipientName"/>
    [Required]
    [StringLength(TrackedShipment.MaxText)]
    public string RecipientName { get; set; } = string.Empty;

    /// <inheritdoc cref="CreateShipmentRequest.RecipientPhone"/>
    [Required]
    [RegularExpression(TrackingLimits.PhonePattern, ErrorMessage = "Phone number is not in a valid format.")]
    public string RecipientPhone { get; set; } = string.Empty;

    /// <inheritdoc cref="CreateShipmentRequest.Address"/>
    [Required]
    [StringLength(TrackedShipment.MaxText)]
    public string Address { get; set; } = string.Empty;

    /// <inheritdoc cref="CreateShipmentRequest.City"/>
    [Required]
    [StringLength(100)]
    public string City { get; set; } = string.Empty;

    /// <inheritdoc cref="CreateShipmentRequest.WeightKg"/>
    [Range(0.1, 70.0, ErrorMessage = "Weight must be between 0.1 and 70 kg.")]
    public decimal WeightKg { get; set; }
}

/// <summary>Body of <c>PATCH /api/shipments/{trackingNumber}/status</c>.</summary>
public sealed class UpdateStatusRequest
{
    /// <summary>The new status, by name (<c>PickedUp</c>) or number. Only the next step of the lifecycle is allowed.</summary>
    /// <example>PickedUp</example>
    [Required]
    [EnumDataType(typeof(TrackingStatus))]
    public TrackingStatus? Status { get; set; }
}

/// <summary>Body of <c>POST /api/shipments/{trackingNumber}/scans</c>: the offline scans a courier recorded (Part A).</summary>
public sealed class ScanUploadRequest
{
    /// <summary>Between 1 and 100 scans. Re-sending a scan with the same <c>scanId</c> never creates a duplicate.</summary>
    [Required]
    [MinLength(1, ErrorMessage = "Send at least one scan.")]
    [MaxLength(TrackingLimits.MaxScansPerUpload, ErrorMessage = "Send at most 100 scans per request.")]
    public List<ScanItemRequest> Scans { get; set; } = [];
}

/// <summary>One scan from the courier's offline store.</summary>
public sealed class ScanItemRequest : IValidatableObject
{
    /// <summary>The scan id generated on the device in Part A. It is the idempotency key.</summary>
    /// <example>3f2b8c1e-6a4d-4e0b-9a51-7d1c2e8f4b10</example>
    [Required]
    public Guid ScanId { get; set; }

    /// <summary>What happened: <c>PickedUp</c>, <c>Arrived</c>, <c>Departed</c>, <c>OutForDelivery</c>, <c>Delivered</c> or <c>DeliveryFailed</c>.</summary>
    /// <example>PickedUp</example>
    [Required]
    [EnumDataType(typeof(ScanType))]
    public ScanType? ScanType { get; set; }

    /// <summary>Depot or street where the scan happened.</summary>
    /// <example>Melbourne Depot</example>
    [Required]
    [StringLength(100)]
    public string Location { get; set; } = string.Empty;

    /// <summary>When the scan happened, in UTC. It must not be in the future.</summary>
    /// <example>2026-11-09T01:30:00Z</example>
    [Required]
    public DateTimeOffset? ScannedAtUtc { get; set; }

    /// <summary>Name the recipient signed with. Required when <c>scanType</c> is <c>Delivered</c>.</summary>
    /// <example>A. Lovelace</example>
    [StringLength(100)]
    public string? RecipientSignatureName { get; set; }

    /// <summary>Custom rules that span several properties (single-property rules are attributes above).</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        var clock = validationContext.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        if (ScanId == Guid.Empty)
        {
            yield return new ValidationResult("ScanId must be a non-empty GUID.", [nameof(ScanId)]);
        }

        // Allow a minute of clock drift between a courier's handset and the server.
        if (ScannedAtUtc is { } scannedAt && scannedAt > clock.GetUtcNow().AddMinutes(1))
        {
            yield return new ValidationResult("A scan cannot be in the future.", [nameof(ScannedAtUtc)]);
        }

        if (ScanType == ApexPost.Models.Domain.ScanType.Delivered && string.IsNullOrWhiteSpace(RecipientSignatureName))
        {
            yield return new ValidationResult("A Delivered scan requires the recipient's signature name.", [nameof(RecipientSignatureName)]);
        }
    }
}
