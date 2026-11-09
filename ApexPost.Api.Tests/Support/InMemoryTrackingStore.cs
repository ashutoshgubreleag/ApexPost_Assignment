using System.Reflection;
using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Tracking;

namespace ApexPost.Api.Tests.Support;

/// <summary>A clock the tests control.</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// An in-memory stand-in for SQL Server behind the three persistence ports. It behaves like EF Core
/// where the rules depend on it: every load returns a NEW object (state is stored as snapshots),
/// changes only become visible on SaveChanges, DiscardChanges forgets staged work, and the unique
/// indexes on TrackingNumber and ScanId throw <see cref="DuplicateKeyException"/>.
/// </summary>
public sealed class InMemoryTrackingStore
{
    private static readonly PropertyInfo IdProperty = typeof(Entity).GetProperty(nameof(Entity.Id))!;

    private readonly Dictionary<int, ShipmentSnapshot> _shipments = [];
    private readonly Dictionary<Guid, int> _scanOwners = [];
    private readonly List<Tracked> _tracked = [];
    private int _nextShipmentId = 1;
    private int _nextScanId = 1;

    public InMemoryTrackingStore()
    {
        Repository = new RepositoryView(this);
        Queries = new QueriesView(this);
        UnitOfWork = new UnitOfWorkView(this);
    }

    public ITrackedShipmentRepository Repository { get; }

    public ITrackingQueries Queries { get; }

    public IUnitOfWork UnitOfWork { get; }

    /// <summary>Runs once, just before the next commit: lets a test simulate another request winning a race.</summary>
    public Action? BeforeNextCommit { get; set; }

    /// <summary>Number of committed SaveChanges calls (a test can prove a rejected request wrote nothing).</summary>
    public int Commits { get; private set; }

    public int StoredScanCount => _scanOwners.Count;

    /// <summary>Stores a scan as if another request had committed it (bypasses the repository on purpose).</summary>
    public void InjectScan(string trackingNumber, Guid scanId, ScanType type = ScanType.PickedUp)
    {
        var shipment = _shipments.Values.Single(s => s.TrackingNumber == trackingNumber);
        shipment.Scans.Add(new ScanSnapshot(_nextScanId++, scanId, type, "Injected", DateTimeOffset.UtcNow.AddHours(-1), null));
        _scanOwners[scanId] = shipment.Id;
    }

    private static void SetProperty(object target, string name, object? value) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

    private TrackedShipment Materialise(ShipmentSnapshot s)
    {
        var entity = (TrackedShipment)Activator.CreateInstance(typeof(TrackedShipment), nonPublic: true)!;
        IdProperty.SetValue(entity, s.Id);
        SetProperty(entity, nameof(TrackedShipment.TrackingNumber), s.TrackingNumber);
        SetProperty(entity, nameof(TrackedShipment.RecipientName), s.RecipientName);
        SetProperty(entity, nameof(TrackedShipment.RecipientPhone), s.RecipientPhone);
        SetProperty(entity, nameof(TrackedShipment.AddressLine), s.AddressLine);
        SetProperty(entity, nameof(TrackedShipment.City), s.City);
        SetProperty(entity, nameof(TrackedShipment.WeightKg), s.WeightKg);
        SetProperty(entity, nameof(TrackedShipment.Status), s.Status);
        SetProperty(entity, nameof(TrackedShipment.CreatedAt), s.CreatedAt);
        foreach (var scan in s.Scans)
        {
            var added = entity.AddScan(scan.ScanId, scan.Type, scan.Location, scan.ScannedAt, scan.Signature);
            IdProperty.SetValue(added, scan.Id);
        }

        _tracked.Add(new Tracked(entity, s.Scans.Count, isNew: false));
        return entity;
    }

    private void Commit()
    {
        BeforeNextCommit?.Invoke();
        BeforeNextCommit = null;

        // 1. Validate against the unique indexes, changing nothing yet (SQL Server rolls the whole save back).
        var newScanIds = new HashSet<Guid>();
        var newTrackingNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tracked in _tracked.Where(t => !t.Removed))
        {
            if (tracked.IsNew
                && (_shipments.Values.Any(s => string.Equals(s.TrackingNumber, tracked.Entity.TrackingNumber, StringComparison.OrdinalIgnoreCase))
                    || !newTrackingNumbers.Add(tracked.Entity.TrackingNumber)))
            {
                throw new DuplicateKeyException("UQ_TrackedShipments_TrackingNumber");
            }

            foreach (var scan in tracked.Entity.Scans.Skip(tracked.LoadedScans))
            {
                if (_scanOwners.ContainsKey(scan.ScanId) || !newScanIds.Add(scan.ScanId))
                {
                    throw new DuplicateKeyException("UQ_ShipmentScans_ScanId");
                }
            }
        }

        // 2. Apply.
        foreach (var tracked in _tracked.ToList())
        {
            var entity = tracked.Entity;
            if (tracked.Removed)
            {
                if (_shipments.Remove(entity.Id, out var gone))
                {
                    foreach (var scan in gone.Scans)
                    {
                        _scanOwners.Remove(scan.ScanId);
                    }
                }

                _tracked.Remove(tracked);
                continue;
            }

            if (tracked.IsNew)
            {
                IdProperty.SetValue(entity, _nextShipmentId++);
            }

            var snapshot = new ShipmentSnapshot(entity.Id)
            {
                TrackingNumber = entity.TrackingNumber,
                RecipientName = entity.RecipientName,
                RecipientPhone = entity.RecipientPhone,
                AddressLine = entity.AddressLine,
                City = entity.City,
                WeightKg = entity.WeightKg,
                Status = entity.Status,
                CreatedAt = entity.CreatedAt
            };
            List<ScanSnapshot> existing = _shipments.TryGetValue(entity.Id, out var old) ? old.Scans : [];
            snapshot.Scans.AddRange(existing);
            foreach (var scan in entity.Scans.Skip(tracked.LoadedScans))
            {
                var id = _nextScanId++;
                IdProperty.SetValue(scan, id);
                snapshot.Scans.Add(new ScanSnapshot(id, scan.ScanId, scan.Type, scan.Location, scan.ScannedAt, scan.RecipientSignatureName));
                _scanOwners[scan.ScanId] = entity.Id;
            }

            _shipments[entity.Id] = snapshot;
            tracked.LoadedScans = entity.Scans.Count;
            tracked.IsNew = false;
        }

        // Like a request-scoped DbContext: after the save the unit of work is over.
        _tracked.Clear();
        Commits++;
    }

    private static ScanDto ToDto(ScanSnapshot s) => new(s.ScanId, s.Type, s.Location, s.ScannedAt, s.Signature);

    private static TrackedShipmentDto ToDto(ShipmentSnapshot s)
    {
        var latest = s.Scans.OrderByDescending(x => x.ScannedAt).ThenByDescending(x => x.Id).FirstOrDefault();
        return new TrackedShipmentDto(s.TrackingNumber, s.RecipientName, s.RecipientPhone, s.AddressLine, s.City, s.WeightKg, s.Status, s.CreatedAt,
            latest is null ? null : ToDto(latest));
    }

    private sealed record ScanSnapshot(int Id, Guid ScanId, ScanType Type, string Location, DateTimeOffset ScannedAt, string? Signature);

    private sealed class ShipmentSnapshot(int id)
    {
        public int Id { get; } = id;
        public string TrackingNumber { get; init; } = string.Empty;
        public string RecipientName { get; init; } = string.Empty;
        public string RecipientPhone { get; init; } = string.Empty;
        public string AddressLine { get; init; } = string.Empty;
        public string City { get; init; } = string.Empty;
        public decimal WeightKg { get; init; }
        public TrackingStatus Status { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public List<ScanSnapshot> Scans { get; } = [];
    }

    private sealed class Tracked(TrackedShipment entity, int loadedScans, bool isNew)
    {
        public TrackedShipment Entity { get; } = entity;
        public int LoadedScans { get; set; } = loadedScans;
        public bool IsNew { get; set; } = isNew;
        public bool Removed { get; set; }
    }

    private sealed class RepositoryView(InMemoryTrackingStore store) : ITrackedShipmentRepository
    {
        public Task<TrackedShipment?> GetByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken)
        {
            var snapshot = store._shipments.Values.SingleOrDefault(s => string.Equals(s.TrackingNumber, trackingNumber, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult<TrackedShipment?>(snapshot is null ? null : store.Materialise(snapshot));
        }

        public Task<bool> ExistsAsync(string trackingNumber, CancellationToken cancellationToken) =>
            Task.FromResult(store._shipments.Values.Any(s => string.Equals(s.TrackingNumber, trackingNumber, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyDictionary<Guid, int>> GetScanOwnersAsync(IReadOnlyCollection<Guid> scanIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(store._scanOwners.Where(kv => scanIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));

        public void Add(TrackedShipment shipment) => store._tracked.Add(new Tracked(shipment, 0, isNew: true));

        public void Remove(TrackedShipment shipment)
        {
            var tracked = store._tracked.Last(t => t.Entity.Id == shipment.Id && !t.IsNew);
            tracked.Removed = true;
        }
    }

    private sealed class QueriesView(InMemoryTrackingStore store) : ITrackingQueries
    {
        public Task<PagedResult<TrackedShipmentDto>> ListAsync(TrackingListQuery query, CancellationToken cancellationToken)
        {
            IEnumerable<ShipmentSnapshot> source = store._shipments.Values;
            if (query.Status is { } status)
            {
                source = source.Where(s => s.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(query.City))
            {
                source = source.Where(s => string.Equals(s.City, query.City, StringComparison.OrdinalIgnoreCase));
            }

            var all = source.OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id).ToList();
            var page = all.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(ToDto).ToList();
            return Task.FromResult(new PagedResult<TrackedShipmentDto>(page, query.Page, query.PageSize, all.Count));
        }

        public Task<TrackedShipmentDto?> GetAsync(string trackingNumber, CancellationToken cancellationToken)
        {
            var snapshot = store._shipments.Values.SingleOrDefault(s => string.Equals(s.TrackingNumber, trackingNumber, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult<TrackedShipmentDto?>(snapshot is null ? null : ToDto(snapshot));
        }

        public Task<IReadOnlyList<ScanDto>?> GetScansAsync(string trackingNumber, CancellationToken cancellationToken)
        {
            var snapshot = store._shipments.Values.SingleOrDefault(s => string.Equals(s.TrackingNumber, trackingNumber, StringComparison.OrdinalIgnoreCase));
            IReadOnlyList<ScanDto>? scans = snapshot?.Scans.OrderBy(s => s.ScannedAt).ThenBy(s => s.Id).Select(ToDto).ToList();
            return Task.FromResult(scans);
        }
    }

    private sealed class UnitOfWorkView(InMemoryTrackingStore store) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            store.Commit();
            return Task.FromResult(1);
        }

        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public void DiscardChanges() => store._tracked.Clear();
    }
}
