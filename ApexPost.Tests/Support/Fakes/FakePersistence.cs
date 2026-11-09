using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Tracking;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Tests.Support.Fakes;

/// <summary>
/// Scoped stand-in for the DbContext: an identity map of tracked copies plus staged inserts.
/// Nothing reaches the store until <see cref="SpyUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public sealed class FakeSession(InMemoryStore store)
{
    private readonly Dictionary<(Type, int), TrackedEntity> _tracked = [];
    private readonly List<object> _added = [];

    internal InMemoryStore Store { get; } = store;

    internal T? Load<T>(int id) where T : class =>
        _tracked.TryGetValue((typeof(T), id), out var hit) ? (T)hit.Entity
        : Store.Find<T>(id) is { } row ? Track(row.Entity, row.Version) : null;

    internal IReadOnlyList<T> LoadWhere<T>(Func<T, bool> predicate) where T : class =>
        Store.Where(predicate).Select(row => _tracked.TryGetValue((typeof(T), InMemoryStore.IdOf(row.Entity)), out var hit) ? (T)hit.Entity : Track(row.Entity, row.Version)).ToList();

    internal void Add(object entity) => _added.Add(entity);

    internal int Commit()
    {
        var count = Store.Commit(_tracked.Values.ToList(), _added);
        foreach (var entity in _added)
        {
            _tracked[(entity.GetType(), InMemoryStore.IdOf(entity))] = new TrackedEntity(entity, 1);
        }

        _added.Clear();
        return count;
    }

    internal void Clear()
    {
        _tracked.Clear();
        _added.Clear();
    }

    private T Track<T>(T stored, long version) where T : class
    {
        var copy = DomainCloner.Clone(stored);
        _tracked[(typeof(T), InMemoryStore.IdOf(copy))] = new TrackedEntity(copy, version);
        return copy;
    }
}

/// <summary>
/// Unit of work for unit tests that also SPIES on how it is used: application-service tests assert
/// "committed exactly once" for a successful use case and "never committed" when a rule fails.
/// </summary>
public sealed class SpyUnitOfWork(FakeSession session) : IUnitOfWork
{
    public int SaveCalls { get; private set; }

    public int TransactionCalls { get; private set; }

    public int DiscardCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCalls++;
        return Task.FromResult(session.Commit());
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        TransactionCalls++;
        return await operation(cancellationToken).ConfigureAwait(false);
    }

    public void DiscardChanges()
    {
        DiscardCalls++;
        session.Clear();
    }
}

internal sealed class FakeBranchRepository(FakeSession session) : IBranchRepository
{
    public Task<Branch?> GetByCodeAsync(BranchCode code, CancellationToken cancellationToken) =>
        Task.FromResult(session.LoadWhere<Branch>(b => b.Code == code).SingleOrDefault());

    public Task<bool> ExistsAsync(BranchCode code, CancellationToken cancellationToken) =>
        Task.FromResult(session.Store.All<Branch>().Any(b => b.Code == code));

    public void Add(Branch branch) => session.Add(branch);
}

internal sealed class FakeStaffRepository(FakeSession session) : IStaffRepository
{
    public Task<StaffMember?> GetByIdAsync(int staffId, CancellationToken cancellationToken) => Task.FromResult(session.Load<StaffMember>(staffId));

    public Task<StaffMember?> GetByEmailAsync(string normalisedEmail, CancellationToken cancellationToken) =>
        Task.FromResult(session.LoadWhere<StaffMember>(s => s.Email == normalisedEmail).SingleOrDefault());

    public Task<bool> EmailInUseAsync(string normalisedEmail, int exceptStaffId, CancellationToken cancellationToken) =>
        Task.FromResult(session.Store.All<StaffMember>().Any(s => s.Email == normalisedEmail && s.Id != exceptStaffId));

    public void Add(StaffMember staff) => session.Add(staff);
}

internal sealed class FakeProductRepository(FakeSession session) : IProductRepository
{
    public Task<Product?> GetBySkuAsync(Sku sku, CancellationToken cancellationToken) =>
        Task.FromResult(session.LoadWhere<Product>(p => p.Sku == sku).SingleOrDefault());

    public Task<IReadOnlyList<Product>> GetBySkusAsync(IReadOnlyCollection<Sku> skus, CancellationToken cancellationToken) =>
        Task.FromResult(session.LoadWhere<Product>(p => skus.Contains(p.Sku)));

    public Task<int> RevisePricesAsync(ProductCategory category, decimal factor, CancellationToken cancellationToken)
    {
        var rows = session.LoadWhere<Product>(p => p.Category == category && p.IsActive);
        foreach (var p in rows)
        {
            p.RevisePrice(new Money(Math.Round(p.UnitPrice.Amount * factor, 2)));
        }

        return Task.FromResult(rows.Count);
    }
}

internal sealed class FakeStockRepository(FakeSession session) : IStockRepository
{
    public Task<IReadOnlyList<BranchStock>> GetForUpdateAsync(BranchCode branch, IReadOnlyCollection<Sku> skus, CancellationToken cancellationToken) =>
        Task.FromResult(session.LoadWhere<BranchStock>(s => s.BranchCode == branch && skus.Contains(s.Sku)));

    public Task<int> GetQuantityAsync(BranchCode branch, Sku sku, CancellationToken cancellationToken) =>
        Task.FromResult(session.Store.All<BranchStock>().Where(s => s.BranchCode == branch && s.Sku == sku).Sum(s => s.QuantityOnHand));

    public void Add(BranchStock stock) => session.Add(stock);
}

internal sealed class FakeStockAdjustmentProcedure(FakeSession session, TimeProvider clock) : IStockAdjustmentProcedure
{
    public Task<int> AdjustAsync(BranchCode branch, Sku sku, int delta, string reason, string changedBy, CancellationToken cancellationToken) =>
        Task.FromResult(session.Store.AdjustStock(branch, sku, delta, clock.GetUtcNow()));
}

internal sealed class FakeCounterTransactionRepository(FakeSession session) : ICounterTransactionRepository
{
    public Task<CounterTransaction?> GetAsync(int transactionId, CancellationToken cancellationToken) => Task.FromResult(session.Load<CounterTransaction>(transactionId));

    public void Add(CounterTransaction transaction) => session.Add(transaction);
}

internal sealed class FakeShipmentRepository(FakeSession session) : IShipmentRepository
{
    public Task<IReadOnlyList<Shipment>> GetByTrackingNumbersAsync(IReadOnlyCollection<string> trackingNumbers, CancellationToken cancellationToken) =>
        Task.FromResult(session.LoadWhere<Shipment>(s => trackingNumbers.Contains(s.TrackingNumber)));

    public void Add(Shipment shipment) => session.Add(shipment);
}

internal sealed class FakeShippingZoneRepository(FakeSession session) : IShippingZoneRepository
{
    public Task<ShippingZone?> GetByCodeAsync(string zoneCode, CancellationToken cancellationToken) =>
        Task.FromResult(session.LoadWhere<ShippingZone>(z => z.ZoneCode == zoneCode).SingleOrDefault());
}

internal sealed class FakeMoneyOrderRepository(FakeSession session) : IMoneyOrderRepository
{
    public Task<MoneyOrder?> GetByIdAsync(int moneyOrderId, CancellationToken cancellationToken) => Task.FromResult(session.Load<MoneyOrder>(moneyOrderId));

    public Task<IReadOnlyList<MoneyOrder>> GetBySerialNumbersAsync(IReadOnlyCollection<string> serialNumbers, CancellationToken cancellationToken) =>
        Task.FromResult(session.LoadWhere<MoneyOrder>(m => serialNumbers.Contains(m.SerialNumber)));

    public void Add(MoneyOrder moneyOrder) => session.Add(moneyOrder);
}

internal sealed class FakeIdentityVerificationRepository(FakeSession session) : IIdentityVerificationRepository
{
    public Task<IReadOnlyList<IdentityVerification>> GetByReferencesAsync(IReadOnlyCollection<string> references, CancellationToken cancellationToken) =>
        Task.FromResult(session.LoadWhere<IdentityVerification>(v => references.Contains(v.Reference)));

    public void Add(IdentityVerification verification) => session.Add(verification);
}

internal sealed class FakeNotificationRepository(FakeSession session) : INotificationRepository
{
    public void Add(Notification notification) => session.Add(notification);
}

internal sealed class FakeSyncInboxRepository(FakeSession session) : ISyncInboxRepository
{
    public Task<SyncInboxEntry?> FindAsync(string deviceId, Guid localTransactionId, CancellationToken cancellationToken) =>
        Task.FromResult(session.Store.All<SyncInboxEntry>().SingleOrDefault(e => e.DeviceId == deviceId && e.LocalTransactionId == localTransactionId));

    public void Add(SyncInboxEntry entry) => session.Add(entry);
}

/// <summary>In-memory audit store (append-only list), used where tests replace the SQL store.</summary>
public sealed class InMemoryAuditStore : IAuditStore
{
    private readonly List<AuditEntry> _entries = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<AuditEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToList();
            }
        }
    }

    public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> ReadRecentAsync(int take, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<AuditEntry>>(_entries.OrderByDescending(e => e.TimestampUtc).Take(take).ToList());
        }
    }
}

public static class FakePersistenceRegistration
{
    /// <summary>Registers the in-memory store, session, spy unit of work, repositories and query services.</summary>
    public static IServiceCollection AddInMemoryPersistence(this IServiceCollection services, InMemoryStore? store = null)
    {
        services.AddSingleton(store ?? new InMemoryStore());
        services.AddScoped<FakeSession>();
        services.AddScoped<SpyUnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SpyUnitOfWork>());
        services.AddScoped<IBranchRepository, FakeBranchRepository>();
        services.AddScoped<IStaffRepository, FakeStaffRepository>();
        services.AddScoped<IProductRepository, FakeProductRepository>();
        services.AddScoped<IStockRepository, FakeStockRepository>();
        services.AddScoped<IStockAdjustmentProcedure, FakeStockAdjustmentProcedure>();
        services.AddScoped<ICounterTransactionRepository, FakeCounterTransactionRepository>();
        services.AddScoped<IShipmentRepository, FakeShipmentRepository>();
        services.AddScoped<IShippingZoneRepository, FakeShippingZoneRepository>();
        services.AddScoped<IMoneyOrderRepository, FakeMoneyOrderRepository>();
        services.AddScoped<IIdentityVerificationRepository, FakeIdentityVerificationRepository>();
        services.AddScoped<INotificationRepository, FakeNotificationRepository>();
        services.AddScoped<ISyncInboxRepository, FakeSyncInboxRepository>();
        // Week 4 tracking ports: the Week 3 suites never use them; the API tests cover them. Present only so the container validates.
        services.AddScoped<ITrackedShipmentRepository, UnusedTrackedShipmentRepository>();
        services.AddScoped<ITrackingQueries, UnusedTrackingQueries>();

        services.AddScoped<InMemoryQueries>();
        // One guard per scope, like the one DbContext per request: overlapping query calls throw.
        services.AddScoped<ScopeOperationGuard>();
        services.AddScoped(sp => GuardedProxy<IBranchQueries>.Create(sp.GetRequiredService<InMemoryQueries>(), sp.GetRequiredService<ScopeOperationGuard>()));
        services.AddScoped(sp => GuardedProxy<IStaffQueries>.Create(sp.GetRequiredService<InMemoryQueries>(), sp.GetRequiredService<ScopeOperationGuard>()));
        services.AddScoped(sp => GuardedProxy<ICatalogQueries>.Create(sp.GetRequiredService<InMemoryQueries>(), sp.GetRequiredService<ScopeOperationGuard>()));
        services.AddScoped(sp => GuardedProxy<ITransactionQueries>.Create(sp.GetRequiredService<InMemoryQueries>(), sp.GetRequiredService<ScopeOperationGuard>()));
        services.AddScoped(sp => GuardedProxy<IReportQueries>.Create(sp.GetRequiredService<InMemoryQueries>(), sp.GetRequiredService<ScopeOperationGuard>()));
        services.AddScoped(sp => GuardedProxy<INotificationQueries>.Create(sp.GetRequiredService<InMemoryQueries>(), sp.GetRequiredService<ScopeOperationGuard>()));
        services.AddScoped(sp => GuardedProxy<ISyncQueries>.Create(sp.GetRequiredService<InMemoryQueries>(), sp.GetRequiredService<ScopeOperationGuard>()));
        services.AddSingleton<InMemoryAuditStore>();
        services.AddSingleton<IAuditStore>(sp => sp.GetRequiredService<InMemoryAuditStore>());
        return services;
    }
}


internal sealed class UnusedTrackedShipmentRepository : ITrackedShipmentRepository
{
    public Task<TrackedShipment?> GetByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> ExistsAsync(string trackingNumber, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<Guid, int>> GetScanOwnersAsync(IReadOnlyCollection<Guid> scanIds, CancellationToken cancellationToken) => throw new NotSupportedException();

    public void Add(TrackedShipment shipment) => throw new NotSupportedException();

    public void Remove(TrackedShipment shipment) => throw new NotSupportedException();
}

internal sealed class UnusedTrackingQueries : ITrackingQueries
{
    public Task<PagedResult<TrackedShipmentDto>> ListAsync(TrackingListQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<TrackedShipmentDto?> GetAsync(string trackingNumber, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ScanDto>?> GetScansAsync(string trackingNumber, CancellationToken cancellationToken) => throw new NotSupportedException();
}
