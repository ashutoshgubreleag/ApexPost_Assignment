using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Services;
using ApexPost.Services.Common;
using ApexPost.Tests.Support.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApexPost.Tests.Support;

/// <summary>A controllable clock so time-based rules are deterministic.</summary>
public sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// UNIT-TEST harness: the real application services (AddApexPostServices) over in-memory fake
/// repositories, a spy unit of work and fake query services. No database is involved.
/// The SQL Server integration test project has a harness with the SAME public surface backed by
/// EF Core and a disposable database, and compiles the very same Week 2 rule-test files against
/// it: identical tests, two persistence layers (that is the refactor proof).
/// </summary>
public sealed class ServiceHarness : IAsyncDisposable
{
    private readonly ServiceProvider _root;

    private ServiceHarness(ServiceProvider root, FakeClock clock)
    {
        _root = root;
        Clock = clock;
    }

    public FakeClock Clock { get; }

    public InMemoryStore Store => _root.GetRequiredService<InMemoryStore>();

    public static Task<ServiceHarness> CreateAsync(IDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Courier:RateLookupLatencyMs"] = "1",
            ["Courier:TrackingLatencyMs"] = "1",
            ["Courier:TrackingTimeoutMs"] = "200",

            // Many parallel "clerks" in the concurrency tests: allow enough optimistic retries.
            ["BusinessRules:ConcurrencyRetryAttempts"] = "50",
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 25, 3, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<TimeProvider>(clock);
        services.AddApexPostServices(configuration);
        services.AddInMemoryPersistence();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        return Task.FromResult(new ServiceHarness(provider, clock));
    }

    public T Get<T>() where T : notnull => _root.GetRequiredService<T>();

    /// <summary>Resolves a scoped service in a fresh scope (like one HTTP request).</summary>
    public T Scoped<T>() where T : notnull => _root.CreateScope().ServiceProvider.GetRequiredService<T>();

    /// <summary>A fresh scope, for tests that need several services (and the spy unit of work) from the same request.</summary>
    public IServiceScope NewScope() => _root.CreateScope();

    /// <summary>True when any stored value contains the text (used to prove secrets are never stored).</summary>
    public Task<bool> StorageContainsAsync(string text) =>
        Task.FromResult(Store.All<IdentityVerification>().Any(v => DomainCloner.Fingerprint(v).Contains(text, StringComparison.Ordinal))
            || Store.All<CounterTransaction>().Any(t => DomainCloner.Fingerprint(t).Contains(text, StringComparison.Ordinal)));

    public static StaffContext Clerk(int id = 7, string branch = "BR-001") => new(id, StaffRole.CounterClerk, new BranchCode(branch), "Test clerk");

    public static StaffContext Manager(int id = 6, string branch = "BR-001") => new(id, StaffRole.BranchManager, new BranchCode(branch), "Test manager");

    public async ValueTask DisposeAsync() => await _root.DisposeAsync().ConfigureAwait(false);
}
