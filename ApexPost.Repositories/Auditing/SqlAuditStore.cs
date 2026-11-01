using System.Text.Json;
using ApexPost.Database;
using ApexPost.Database.Records;
using ApexPost.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Repositories.Auditing;

/// <summary>
/// Writes audit entries to the AuditEntries table through its OWN short-lived DbContext (from
/// IDbContextFactory), not the request's unit of work (ADR-W3-07):
///   - a failed or rolled-back business transaction still leaves its audit row (the filter
///     records "Failed"), which is exactly when auditors need it;
///   - an audit write problem can never roll back or block a sale;
///   - the audit row is not accidentally committed by somebody else's SaveChanges.
/// The entry has already been whitelisted and redacted by the audit filter; this class only stores it.
/// </summary>
public sealed class SqlAuditStore(IDbContextFactory<ApexPostDbContext> factory) : IAuditStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.AuditEntries.Add(new AuditEntryRecord
        {
            Id = entry.Id,
            TimestampUtc = entry.TimestampUtc.UtcDateTime,
            Action = entry.Action,
            StaffId = entry.StaffId,
            Role = entry.Role,
            BranchCode = entry.BranchCode,
            Controller = entry.Controller,
            ControllerAction = entry.ControllerAction,
            HttpMethod = entry.HttpMethod,
            Path = Truncate(entry.Path, 400),
            RouteValuesJson = Truncate(JsonSerializer.Serialize(entry.RouteValues, Json), 2000),
            FieldsJson = Truncate(JsonSerializer.Serialize(entry.Fields, Json), 4000),
            StatusCode = entry.StatusCode,
            Outcome = entry.Outcome,
            Error = entry.Error is null ? null : Truncate(entry.Error, 1000),
            DurationMs = entry.DurationMs,
            CorrelationId = entry.CorrelationId,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEntry>> ReadRecentAsync(int take, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.AuditEntries.AsNoTracking()
            .OrderByDescending(a => a.TimestampUtc)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.Select(r => new AuditEntry
        {
            Id = r.Id,
            TimestampUtc = new DateTimeOffset(DateTime.SpecifyKind(r.TimestampUtc, DateTimeKind.Utc)),
            Action = r.Action,
            StaffId = r.StaffId,
            Role = r.Role,
            BranchCode = r.BranchCode,
            Controller = r.Controller,
            ControllerAction = r.ControllerAction,
            HttpMethod = r.HttpMethod,
            Path = r.Path,
            RouteValues = JsonSerializer.Deserialize<Dictionary<string, string>>(r.RouteValuesJson, Json) ?? [],
            Fields = JsonSerializer.Deserialize<Dictionary<string, string>>(r.FieldsJson, Json) ?? [],
            StatusCode = r.StatusCode,
            Outcome = r.Outcome,
            Error = r.Error,
            DurationMs = r.DurationMs,
            CorrelationId = r.CorrelationId,
        }).ToList();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
