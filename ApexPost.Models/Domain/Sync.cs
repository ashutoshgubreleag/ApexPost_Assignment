using ApexPost.Models.ValueObjects;

namespace ApexPost.Models.Domain;

/// <summary>
/// One offline sale received from a counter terminal. The unique (DeviceId, LocalTransactionId)
/// index makes the sync endpoint idempotent: a retry finds this row and gets the original result
/// back instead of posting the sale twice.
/// </summary>
public sealed class SyncInboxEntry : Entity
{
    private readonly List<SyncConflict> _conflicts = [];

    private SyncInboxEntry()
    {
    }

    public string DeviceId { get; private set; } = string.Empty;

    public Guid LocalTransactionId { get; private set; }

    public BranchCode BranchCode { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public SyncOutcome Outcome { get; private set; }

    /// <summary>The CounterTransactions row created for this sale (null when rejected).</summary>
    public int? ServerTransactionId { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public IReadOnlyCollection<SyncConflict> Conflicts => _conflicts.AsReadOnly();

    public static SyncInboxEntry Receive(string deviceId, Guid localTransactionId, BranchCode branch, DateTimeOffset at) =>
        new()
        {
            DeviceId = Guard.Text(deviceId, nameof(deviceId), 50),
            LocalTransactionId = localTransactionId,
            BranchCode = branch,
            ReceivedAt = at,
            Outcome = SyncOutcome.Accepted,
        };

    public void Accept(CounterTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        LinkedTransaction = transaction;
        Outcome = _conflicts.Count == 0 ? SyncOutcome.Accepted : SyncOutcome.AcceptedWithWarnings;
        Message = _conflicts.Count == 0 ? "Accepted." : string.Join(" ", _conflicts.Select(c => c.Details));
    }

    public void Reject(string reason)
    {
        Outcome = SyncOutcome.Rejected;
        Message = Guard.Text(reason, nameof(reason), 400);
    }

    public SyncConflict Flag(SyncConflictType type, string itemCode, string details, DateTimeOffset at)
    {
        var conflict = new SyncConflict(type, itemCode, details, at);
        _conflicts.Add(conflict);
        return conflict;
    }

    /// <summary>Copies the new transaction's key after the unit of work has committed.</summary>
    public void CaptureTransactionId()
    {
        if (LinkedTransaction is { IsTransient: false } tx)
        {
            ServerTransactionId = tx.Id;
        }
    }

    /// <summary>Navigation to the sale created from this entry (lets EF insert both in one SaveChanges).</summary>
    public CounterTransaction? LinkedTransaction { get; private set; }
}

/// <summary>A conflict raised while applying an offline sale (CONFLICTS.md). Reviewed by a manager.</summary>
public sealed class SyncConflict : Entity
{
    private SyncConflict()
    {
    }

    internal SyncConflict(SyncConflictType type, string itemCode, string details, DateTimeOffset at)
    {
        Type = type;
        ItemCode = Guard.Text(itemCode, nameof(itemCode), 30);
        Details = Guard.Text(details, nameof(details), 400);
        RaisedAt = at;
        Status = SyncConflictStatus.Open;
    }

    public int SyncInboxEntryId { get; private set; }

    public SyncConflictType Type { get; private set; }

    public string ItemCode { get; private set; } = string.Empty;

    public string Details { get; private set; } = string.Empty;

    public SyncConflictStatus Status { get; private set; }

    public DateTimeOffset RaisedAt { get; private set; }

    public void Resolve() => Status = SyncConflictStatus.Resolved;
}

/// <summary>A simulated notification (would be an email/SMS to the branch manager in production).</summary>
public sealed class Notification : Entity
{
    private Notification()
    {
    }

    public NotificationType Type { get; private set; }

    public BranchCode? BranchCode { get; private set; }

    public string Recipient { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public string CorrelationId { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Notification Create(NotificationType type, BranchCode? branch, string recipient, string message, string correlationId, DateTimeOffset at) =>
        new()
        {
            Type = type,
            BranchCode = branch,
            Recipient = Guard.OptionalText(recipient, nameof(recipient), 254),
            Message = Guard.Text(message, nameof(message), 500),
            CorrelationId = Guard.OptionalText(correlationId, nameof(correlationId), 64),
            CreatedAt = at,
        };
}
