using ApexPost.Api.Tests.Support;
using ApexPost.Courier.Offline.Application;
using ApexPost.Services.Tracking;
using Microsoft.Extensions.Options;
using OfflineScan = ApexPost.Courier.Offline.Domain.ShipmentScan;

namespace ApexPost.Specs.Support;

/// <summary>
/// Everything one scenario knows. Reqnroll creates ONE instance per scenario and injects it into every step and hook
/// class that asks for it (context injection), so steps share state without static fields and scenarios can never
/// leak into each other. It is filled by <see cref="Hooks"/> before each scenario and cleared after it.
/// </summary>
public sealed class ShipmentWorld
{
    /// <summary>The server's "database": an in-memory store that behaves like EF Core (snapshots, unique indexes, discard).</summary>
    public InMemoryTrackingStore Store { get; private set; } = null!;

    /// <summary>The business clock. Steps such as "today is ..." move it; nothing reads the wall clock.</summary>
    public TestClock Clock { get; private set; } = null!;

    /// <summary>The real server-side business service under test.</summary>
    public ShipmentTrackingService Tracking { get; private set; } = null!;

    /// <summary>The courier phone's local scan store (in memory).</summary>
    public InMemoryScanRepository Phone { get; private set; } = null!;

    /// <summary>The link between phone and server (in-process, no network).</summary>
    public InProcessUploader Uploader { get; private set; } = null!;

    /// <summary>The real courier outbox under test.</summary>
    public OutboxService Outbox { get; private set; } = null!;

    /// <summary>The parcel the scenario is about (set by "parcel ... is registered for delivery").</summary>
    public string CurrentParcel { get; set; } = string.Empty;

    /// <summary>The courier the phone belongs to.</summary>
    public string CourierId { get; set; } = "C-1001";

    /// <summary>Scans recorded in this scenario, by the name the scenario gave them.</summary>
    public Dictionary<string, OfflineScan> NamedScans { get; } = new(StringComparer.Ordinal);

    /// <summary>The exception of the last business action, if it was refused.</summary>
    public Exception? LastError { get; set; }

    public ShipmentChargesDto? LastCharges { get; set; }

    public OutboxSyncResult? LastSync { get; set; }

    /// <summary>Builds a fresh, empty world. Called by the BeforeScenario hook.</summary>
    public void Start(DateTimeOffset today)
    {
        Store = new InMemoryTrackingStore();
        Clock = new TestClock(today);
        var surcharges = new HeavyParcelSurchargePolicy(Options.Create(new HeavyParcelSurchargeOptions()), Clock);
        Tracking = new ShipmentTrackingService(Store.Repository, Store.Queries, Store.UnitOfWork, Clock, surcharges);
        Phone = new InMemoryScanRepository();
        Uploader = new InProcessUploader(() => Tracking);
        Outbox = new OutboxService(Phone, Uploader);
    }

    /// <summary>Forgets everything the scenario created. Called by the AfterScenario hook.</summary>
    public void Clear()
    {
        NamedScans.Clear();
        Phone?.Clear();
        LastError = null;
        LastCharges = null;
        LastSync = null;
    }

    /// <summary>Moves the business clock to a given calendar day (08:00 UTC).</summary>
    public void SetToday(DateTimeOffset day) => Clock.Advance(day - Clock.GetUtcNow());

    /// <summary>Runs a business action and remembers a refusal instead of failing the step, so a Then step can check it.</summary>
    public async Task TryAsync(Func<Task> action)
    {
        LastError = null;
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is ApexPost.Models.Exceptions.DomainException)
        {
            LastError = ex;
        }
    }
}
