using ApexPost.Models.ValueObjects;

namespace ApexPost.Database.Records;

/// <summary>
/// Persistence model for an identity verification (separate from the domain object, see ADR-W3-05).
/// The table stores only the LAST FOUR characters of the document number in a char(4) column,
/// so even a bug elsewhere cannot write a full number: the schema has nowhere to put it. The
/// domain's "****1234" display form is rebuilt by the mapper. Enum values are stored as the
/// legacy-style codes used by the compliance system (PP / DL / NI, PASS / FAIL).
/// </summary>
public sealed class IdentityVerificationRecord
{
    public int Id { get; set; }

    public string Reference { get; set; } = string.Empty;

    public int TransactionId { get; set; }

    public BranchCode BranchCode { get; set; }

    public int StaffId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    /// <summary>PP, DL or NI.</summary>
    public string DocumentTypeCode { get; set; } = string.Empty;

    /// <summary>Exactly four characters: the last four of the document number.</summary>
    public string DocumentLast4 { get; set; } = string.Empty;

    /// <summary>PASS or FAIL.</summary>
    public string ResultCode { get; set; } = string.Empty;

    public DateTime VerifiedAtUtc { get; set; }
}

/// <summary>One row of the AuditEntries table (written by the audit filter, never updated).</summary>
public sealed class AuditEntryRecord
{
    public Guid Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? StaffId { get; set; }

    public string? Role { get; set; }

    public string? BranchCode { get; set; }

    public string Controller { get; set; } = string.Empty;

    public string ControllerAction { get; set; } = string.Empty;

    public string HttpMethod { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    /// <summary>Route values as JSON (whitelisted keys only).</summary>
    public string RouteValuesJson { get; set; } = "{}";

    /// <summary>Whitelisted, redacted form fields as JSON.</summary>
    public string FieldsJson { get; set; } = "{}";

    public int StatusCode { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public string? Error { get; set; }

    public long DurationMs { get; set; }

    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>History row written by usp_AdjustStock for every manual stock adjustment.</summary>
public sealed class StockAdjustmentRecord
{
    public int Id { get; set; }

    public BranchCode BranchCode { get; set; }

    public Sku Sku { get; set; }

    public int Delta { get; set; }

    public int QuantityAfter { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string ChangedBy { get; set; } = string.Empty;

    public DateTime ChangedAtUtc { get; set; }
}
