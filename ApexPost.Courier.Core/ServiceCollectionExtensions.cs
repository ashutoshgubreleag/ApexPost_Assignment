using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Core.Auth;
using ApexPost.Courier.Core.Resilience;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Offline.Application;
using ApexPost.Courier.Offline.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ApexPost.Courier.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the typed API client (IHttpClientFactory) with the auth handler and the Polly pipeline, the real uploader,
    /// the outbox and the sync coordinator. The caller registers <see cref="IScanRepository"/>, <see cref="ITokenStore"/>
    /// and <see cref="INetworkStatus"/> (the MAUI app uses Couchbase Lite, SecureStorage and MAUI Connectivity).
    /// Returns the client builder so tests can swap the primary HTTP handler.
    /// </summary>
    public static IHttpClientBuilder AddApexCourierCore(this IServiceCollection services, Action<ApiOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ApiOptions();
        configure(options);
        services.AddSingleton(Options.Create(options));

        var monitor = new CircuitStateMonitor();
        services.AddSingleton(monitor);
        services.AddSingleton<ICircuitStateMonitor>(monitor);

        services.TryAddTransient<AuthHeaderHandler>();
        services.AddSingleton<IShipmentUploader, ApiShipmentUploader>();
        services.AddSingleton(sp => new OutboxService(sp.GetRequiredService<IScanRepository>(), sp.GetRequiredService<IShipmentUploader>()));
        services.AddSingleton<SyncCoordinator>();

        // Order matters: the auth handler is outermost, the resilience pipeline is next to the network.
        return services
            .AddHttpClient<ApexApiClient>(client =>
            {
                client.BaseAddress = options.BaseAddress;
                client.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns the timeouts
            })
            .AddHttpMessageHandler<AuthHeaderHandler>()
            .AddApexResilience(options.Resilience, monitor);
    }
}
