using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ApexPost.Database.Conventions;

/// <summary>Who is making the change (staff id or "system"); the web layer supplies it from the signed-in user.</summary>
public interface IAuditActor
{
    string Name { get; }
}

public sealed class SystemAuditActor : IAuditActor
{
    public string Name => "system";
}

/// <summary>
/// Fills the CreatedAtUtc / CreatedBy / ModifiedAtUtc shadow columns on every SaveChanges, so the
/// domain entities never carry persistence bookkeeping. CreatedAtUtc/CreatedBy are never
/// overwritten after insert.
/// </summary>
public sealed class AuditColumnsInterceptor(IAuditActor actor, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Metadata.FindProperty(ModelBuilderExtensions.CreatedAtUtc) is null)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(ModelBuilderExtensions.CreatedAtUtc).CurrentValue = now;
                    entry.Property(ModelBuilderExtensions.CreatedBy).CurrentValue = Truncate(actor.Name);
                    break;
                case EntityState.Modified:
                    entry.Property(ModelBuilderExtensions.ModifiedAtUtc).CurrentValue = now;
                    entry.Property(ModelBuilderExtensions.CreatedAtUtc).IsModified = false;
                    entry.Property(ModelBuilderExtensions.CreatedBy).IsModified = false;
                    break;
            }
        }
    }

    private static string Truncate(string value) => value.Length <= 50 ? value : value[..50];
}
