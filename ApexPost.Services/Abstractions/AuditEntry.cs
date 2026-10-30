namespace ApexPost.Utilities.Audit;

/// <summary>
/// One audit record: who (staff, role, branch) did what (action, key values), when, with what
/// outcome, and the correlation ID that ties it to request logs and notifications.
/// Immutable: an audit entry is never edited after it is written.
/// </summary>
public sealed record AuditEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Business action name, e.g. <c>Transaction.Pay</c>, <c>Stock.Adjust</c>.</summary>
    public string Action { get; init; } = string.Empty;

    public string? StaffId { get; init; }
    public string? Role { get; init; }
    public string? BranchCode { get; init; }

    public string Controller { get; init; } = string.Empty;
    public string ControllerAction { get; init; } = string.Empty;
    public string HttpMethod { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;

    /// <summary>Route values such as the transaction id.</summary>
    public IReadOnlyDictionary<string, string> RouteValues { get; init; } = new Dictionary<string, string>();

    /// <summary>Whitelisted, redacted input fields (never passwords, document numbers, keys or card data).</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    public int StatusCode { get; init; }

    /// <summary>Succeeded, Rejected (4xx business/validation/forbidden) or Failed (5xx).</summary>
    public string Outcome { get; init; } = string.Empty;

    /// <summary>Business rule code or exception type when the action did not succeed.</summary>
    public string? Error { get; init; }

    public long DurationMs { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
}
