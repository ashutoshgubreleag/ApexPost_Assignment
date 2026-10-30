using ApexPost.Database;
using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Repositories;
using ApexPost.Services;
using ApexPost.Services.Common;
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
/// SQL SERVER harness with the same public surface as the unit-test harness in ApexPost.Tests:
/// the same application services (AddApexPostServices), but the real EF Core repositories, unit of
/// work and query services (AddApexPostPersistence) over a disposable, migrated database.
/// </summary>
public sealed class ServiceHarness : IAsyncDisposable
{
    private readonly ServiceProvider _root;

    private ServiceHarness(ServiceProvider root, FakeClock clock, SqlTestDatabase database)
    {
        _root = root;
        Clock = clock;
        Database = database;
    }

    public FakeClock Clock { get; }

    public SqlTestDatabase Database { get; }

    public static async Task<ServiceHarness> CreateAsync(IDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Courier:RateLookupLatencyMs"] = "1",
            ["Courier:TrackingLatencyMs"] = "1",
            ["Courier:TrackingTimeoutMs"] = "200",
            ["BusinessRules:ConcurrencyRetryAttempts"] = "50",
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(false);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 25, 3, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<TimeProvider>(clock);
        services.AddApexPostServices(configuration);
        services.AddApexPostPersistence(_ => new PersistenceOptions { ConnectionString = database.ConnectionString, MaxRetryCount = 0 });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        return new ServiceHarness(provider, clock, database);
    }

    public T Get<T>() where T : notnull => _root.GetRequiredService<T>();

    /// <summary>Resolves a scoped service in a fresh scope (like one HTTP request, with its own DbContext).</summary>
    public T Scoped<T>() where T : notnull => _root.CreateScope().ServiceProvider.GetRequiredService<T>();

    public IServiceScope NewScope() => _root.CreateScope();

    /// <summary>A separate context for assertions, so nothing is read from the change tracker of the code under test.</summary>
    public ApexPostDbContext NewContext() => Database.NewContext();

    public static StaffContext Clerk(int id = 7, string branch = "BR-001") => new(id, StaffRole.CounterClerk, new BranchCode(branch), "Test clerk");

    public static StaffContext Manager(int id = 6, string branch = "BR-001") => new(id, StaffRole.BranchManager, new BranchCode(branch), "Test manager");

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync().ConfigureAwait(false);
        await Database.DisposeAsync().ConfigureAwait(false);
    }
}
