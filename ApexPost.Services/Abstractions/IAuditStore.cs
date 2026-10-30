namespace ApexPost.Utilities.Audit;

/// <summary>Append-only audit log. There is deliberately no update or delete.</summary>
public interface IAuditStore
{
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken);

    /// <summary>Most recent entries first (for the auditor screen).</summary>
    Task<IReadOnlyList<AuditEntry>> ReadRecentAsync(int take, CancellationToken cancellationToken);
}
