using ApexPost.Database.Records;
using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;

namespace ApexPost.Repositories.Persistence;

/// <summary>
/// Explicit mapper between the domain <see cref="IdentityVerification"/> and its storage shape.
/// Storage keeps only the last four characters and legacy codes; the domain keeps the masked
/// value object and enums. Masking is enforced twice: by <see cref="MaskedDocumentNumber"/> in the
/// domain and by the char(4) column + CHECK constraint in the schema.
/// </summary>
public static class IdentityVerificationMapper
{
    public static IdentityVerificationRecord ToRecord(IdentityVerification v)
    {
        ArgumentNullException.ThrowIfNull(v);
        return new IdentityVerificationRecord
        {
            Id = v.Id,
            Reference = v.Reference,
            TransactionId = v.TransactionId,
            BranchCode = v.BranchCode,
            StaffId = v.StaffId,
            CustomerName = v.CustomerName,
            DocumentTypeCode = v.DocumentType switch
            {
                DocumentType.Passport => "PP",
                DocumentType.DriverLicence => "DL",
                DocumentType.NationalId => "NI",
                _ => throw new ArgumentOutOfRangeException(nameof(v), v.DocumentType, "Unknown document type."),
            },
            DocumentLast4 = v.MaskedDocumentNumber.Value[^4..],
            ResultCode = v.Result == VerificationResult.Passed ? "PASS" : "FAIL",
            VerifiedAtUtc = v.VerifiedAt.UtcDateTime,
        };
    }

    public static IdentityVerification ToDomain(IdentityVerificationRecord r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return IdentityVerification.Rehydrate(
            r.Id,
            r.Reference,
            r.TransactionId,
            r.BranchCode,
            r.StaffId,
            r.CustomerName,
            r.DocumentTypeCode switch
            {
                "PP" => DocumentType.Passport,
                "DL" => DocumentType.DriverLicence,
                "NI" => DocumentType.NationalId,
                _ => throw new InvalidOperationException($"Unknown document type code '{r.DocumentTypeCode}'."),
            },
            MaskedDocumentNumber.FromMasked("****" + r.DocumentLast4),
            r.ResultCode == "PASS" ? VerificationResult.Passed : VerificationResult.Failed,
            new DateTimeOffset(DateTime.SpecifyKind(r.VerifiedAtUtc, DateTimeKind.Utc)));
    }

    /// <summary>Record → response DTO for the read side (via the domain, so masking rules apply once).</summary>
    public static VerificationDto ToDto(IdentityVerificationRecord r)
    {
        var v = ToDomain(r);
        return new VerificationDto(v.Id, v.Reference, v.TransactionId, v.BranchCode, v.StaffId, v.CustomerName, v.DocumentType, v.MaskedDocumentNumber, v.Result, v.VerifiedAt);
    }
}
