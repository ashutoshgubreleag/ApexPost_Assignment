using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ApexPost.Services.Abstractions;

namespace ApexPost.Api.Tests.Support;

/// <summary>
/// Hosts the real ApexPost.Api pipeline (routing, model binding, validation, exception handler) in
/// memory. Only the three persistence ports are replaced, with <see cref="InMemoryTrackingStore"/>, so no SQL Server is needed.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The "database" behind this host.</summary>
    public InMemoryTrackingStore Store { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ApexPostDb"] = "Server=unused-in-api-tests",
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITrackedShipmentRepository>();
            services.RemoveAll<ITrackingQueries>();
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton(Store.Repository);
            services.AddSingleton(Store.Queries);
            services.AddSingleton(Store.UnitOfWork);
        });
    }
}

/// <summary>A query service that always fails, to prove unexpected errors come back as a safe 500.</summary>
public sealed class ThrowingQueries : ITrackingQueries
{
    public const string SecretMessage = "Server=prod-db;Password=hunter2";

    public Task<ApexPost.Services.Tracking.PagedResult<ApexPost.Services.Tracking.TrackedShipmentDto>> ListAsync(ApexPost.Services.Tracking.TrackingListQuery query, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(SecretMessage);

    public Task<ApexPost.Services.Tracking.TrackedShipmentDto?> GetAsync(string trackingNumber, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(SecretMessage);

    public Task<IReadOnlyList<ApexPost.Services.Tracking.ScanDto>?> GetScansAsync(string trackingNumber, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(SecretMessage);
}
