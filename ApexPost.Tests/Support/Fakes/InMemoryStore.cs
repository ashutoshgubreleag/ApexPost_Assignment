using System.Reflection;
using ApexPost.Database.Seeding;
using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;

namespace ApexPost.Tests.Support.Fakes;

/// <summary>
/// The fake "database": committed domain objects per aggregate type, each with a version number
/// (the rowversion stand-in). Objects in the store are never mutated after they are stored;
/// sessions work on clones and commit by replacing them, under one lock, after a version check.
/// Seeded from the same embedded reference data as the InitialCreate migration.
/// </summary>
public sealed class InMemoryStore
{
    private static readonly HashSet<Type> Versioned = [typeof(BranchStock), typeof(CounterTransaction)];

    private readonly object _gate = new();
    private readonly Dictionary<Type, Dictionary<int, Row>> _tables = [];
    private readonly Dictionary<Type, int> _nextIds = [];

    public InMemoryStore() => Seed();

    /// <summary>Every write, in order (lets tests prove a failed use case committed nothing).</summary>
    public int CommitCount { get; private set; }

    public IReadOnlyList<T> All<T>() where T : class
    {
        lock (_gate)
        {
            return Table(typeof(T)).Values.Select(r => (T)r.Entity).ToList();
        }
    }

    internal (T Entity, long Version)? Find<T>(int id) where T : class
    {
        lock (_gate)
        {
            return Table(typeof(T)).TryGetValue(id, out var row) ? ((T)row.Entity, row.Version) : null;
        }
    }

    internal IReadOnlyList<(T Entity, long Version)> Where<T>(Func<T, bool> predicate) where T : class
    {
        lock (_gate)
        {
            return Table(typeof(T)).Values.Select(r => ((T)r.Entity, r.Version)).Where(x => predicate(x.Item1)).ToList();
        }
    }

    /// <summary>Atomically applies one session's changes, or throws without applying any.</summary>
    internal int Commit(IReadOnlyList<TrackedEntity> tracked, IReadOnlyList<object> added)
    {
        lock (_gate)
        {
            var modified = tracked.Where(t => t.IsModified).ToList();
            foreach (var t in modified.Where(t => Versioned.Contains(t.Entity.GetType())))
            {
                var current = Table(t.Entity.GetType())[IdOf(t.Entity)];
                if (current.Version != t.Version)
                {
                    throw new ConcurrencyConflictException("Another counter changed this record at the same time (fake rowversion check).");
                }
            }

            foreach (var entry in added.OfType<SyncInboxEntry>())
            {
                if (Table(typeof(SyncInboxEntry)).Values.Select(r => (SyncInboxEntry)r.Entity)
                    .Any(e => e.DeviceId == entry.DeviceId && e.LocalTransactionId == entry.LocalTransactionId))
                {
                    throw new DuplicateKeyException("UQ_SyncInbox_Device_LocalTransaction");
                }
            }

            // Keys first (principals before dependents), then foreign-key fix-up, then store copies.
            foreach (var entity in added.OrderBy(e => e is CounterTransaction ? 0 : 1))
            {
                AssignKeys(entity);
            }

            foreach (var entity in added.Concat(modified.Select(m => m.Entity)))
            {
                FixUp(entity);
            }

            foreach (var entity in added)
            {
                Store(entity, 1);
            }

            foreach (var t in modified)
            {
                Store(t.Entity, t.Version + 1);
                t.Version++;
                t.ResetFingerprint();
            }

            CommitCount++;
            return added.Count + modified.Count;
        }
    }

    /// <summary>Applies a stock adjustment atomically (the fake of usp_AdjustStock).</summary>
    internal int AdjustStock(BranchCode branch, Sku sku, int delta, DateTimeOffset at)
    {
        lock (_gate)
        {
            var row = Table(typeof(BranchStock)).Values.FirstOrDefault(r => ((BranchStock)r.Entity).BranchCode == branch && ((BranchStock)r.Entity).Sku == sku);
            if (row is null)
            {
                if (delta < 0)
                {
                    throw new BusinessRuleViolationException("STOCK_INSUFFICIENT", "There is no stock of this SKU at this branch to write off.");
                }

                var created = BranchStock.Create(branch, sku, delta, 5, at);
                AssignKeys(created);
                Store(created, 1);
                return delta;
            }

            var copy = DomainCloner.Clone((BranchStock)row.Entity);
            if (delta > 0)
            {
                copy.Add(delta, at);
            }
            else
            {
                copy.Remove(-delta, at);
            }

            Store(copy, row.Version + 1);
            return copy.QuantityOnHand;
        }
    }

    internal static int IdOf(object entity) => entity switch
    {
        Entity e => e.Id,
        _ => throw new InvalidOperationException($"{entity.GetType().Name} is not a root entity."),
    };

    private void Store(object entity, long version)
    {
        var copy = DomainCloner.Clone(entity);
        ClearNavigation(copy);
        Table(copy.GetType())[IdOf(copy)] = new Row(copy, version);
    }

    private void AssignKeys(object entity)
    {
        if (entity is Entity { Id: 0 } root)
        {
            SetId(root, NextId(root.GetType()));
        }

        switch (entity)
        {
            case Branch branch:
                foreach (var counter in branch.Counters.Where(c => c.Id == 0))
                {
                    SetId(counter, NextId(typeof(Counter)));
                }

                break;
            case CounterTransaction tx:
                foreach (var payment in tx.Payments.Where(p => p.Id == 0))
                {
                    SetId(payment, NextId(typeof(Payment)));
                }

                break;
            case SyncInboxEntry inbox:
                foreach (var conflict in inbox.Conflicts.Where(c => c.Id == 0))
                {
                    SetId(conflict, NextId(typeof(SyncConflict)));
                }

                break;
        }
    }

    private static void FixUp(object entity)
    {
        switch (entity)
        {
            case CounterTransaction tx:
                foreach (var line in tx.LineItems)
                {
                    Set(line, nameof(TransactionLine.TransactionId), tx.Id);
                }

                foreach (var payment in tx.Payments)
                {
                    Set(payment, nameof(Payment.TransactionId), tx.Id);
                }

                break;
            case MoneyOrder { Transaction: { } tx } order:
                Set(order, nameof(MoneyOrder.TransactionId), tx.Id);
                break;
            case SyncInboxEntry inbox:
                if (inbox.LinkedTransaction is { } linked)
                {
                    Set(inbox, nameof(SyncInboxEntry.ServerTransactionId), linked.Id);
                }

                foreach (var conflict in inbox.Conflicts)
                {
                    Set(conflict, nameof(SyncConflict.SyncInboxEntryId), inbox.Id);
                }

                break;
        }
    }

    private static void ClearNavigation(object entity)
    {
        switch (entity)
        {
            case MoneyOrder order:
                Set(order, nameof(MoneyOrder.Transaction), null);
                break;
        }
    }

    private int NextId(Type type)
    {
        _nextIds.TryGetValue(type, out var next);
        _nextIds[type] = ++next;
        return next;
    }

    private Dictionary<int, Row> Table(Type type)
    {
        if (!_tables.TryGetValue(type, out var table))
        {
            table = [];
            _tables[type] = table;
        }

        return table;
    }

    internal static void SetId(Entity entity, int id) => Set(entity, nameof(Entity.Id), id);

    internal static void Set(object target, string property, object? value)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            var prop = type.GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (prop?.SetMethod is not null)
            {
                prop.SetValue(target, value);
                return;
            }
        }

        throw new InvalidOperationException($"{target.GetType().Name}.{property} has no setter.");
    }

    private void Seed()
    {
        var at = new DateTimeOffset(ReferenceData.SeededAtUtc, TimeSpan.Zero);
        foreach (var b in ReferenceData.Branches)
        {
            var branch = Branch.Create(new BranchCode(b.BranchCode), b.Name, b.Region, b.OpeningHours, b.Counters.Count);
            if (b.Status == nameof(BranchStatus.Closed))
            {
                branch.SetStatus(BranchStatus.Closed);
            }

            SetId(branch, b.Id);
            AssignKeys(branch);
            Store(branch, 1);
        }

        foreach (var s in ReferenceData.Staff)
        {
            var member = StaffMember.Create(s.Name, s.Email, Enum.Parse<StaffRole>(s.Role), new BranchCode(s.HomeBranchCode), s.PasswordHash);
            if (!s.IsActive)
            {
                member.Deactivate();
            }

            SetId(member, s.Id);
            Store(member, 1);
        }

        foreach (var p in ReferenceData.Products)
        {
            var product = Product.Create(new Sku(p.Sku), p.Name, Enum.Parse<ProductCategory>(p.Category), new Money(p.UnitPrice), Enum.Parse<TaxCategory>(p.TaxCategory));
            if (!p.IsActive)
            {
                product.Deactivate();
            }

            SetId(product, p.Id);
            Store(product, 1);
        }

        foreach (var s in ReferenceData.Stock)
        {
            var stock = BranchStock.Create(new BranchCode(s.BranchCode), new Sku(s.Sku), s.QuantityOnHand, s.ReorderThreshold, s.UpdatedAt);
            SetId(stock, s.Id);
            Store(stock, 1);
        }

        foreach (var z in ReferenceData.Zones)
        {
            var zone = ShippingZone.Create(z.ZoneCode, z.Name, z.IsInternational, z.IsRemote, new Money(z.LetterBaseRate), new Money(z.ParcelBaseRate), new Money(z.PerKgRate));
            SetId(zone, z.Id);
            Store(zone, 1);
        }

        foreach (var type in _tables.Keys)
        {
            _nextIds[type] = _tables[type].Keys.DefaultIfEmpty(0).Max();
        }

        _ = at;
    }

    private sealed record Row(object Entity, long Version);
}

/// <summary>An entity a session loaded for update: its copy, the version it saw and how it looked then.</summary>
internal sealed class TrackedEntity(object entity, long version)
{
    private string _fingerprint = DomainCloner.Fingerprint(entity);

    public object Entity { get; } = entity;

    public long Version { get; set; } = version;

    public bool IsModified => DomainCloner.Fingerprint(Entity) != _fingerprint;

    public void ResetFingerprint() => _fingerprint = DomainCloner.Fingerprint(Entity);
}
