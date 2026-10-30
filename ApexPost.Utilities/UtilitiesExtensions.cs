using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ApexPost.Utilities.Filters;
using ApexPost.Utilities.Options;

namespace ApexPost.Utilities;

public static class UtilitiesExtensions
{
    /// <summary>
    /// Registers cross-cutting services. All are singletons: middleware is constructed once for the
    /// app's lifetime, so anything injected into a middleware constructor must be a singleton too
    /// (a scoped service captured there would leak one request's state into every later request).
    /// Filters added with <c>AddService</c> are resolved from DI with the same rule.
    /// </summary>
    public static IServiceCollection AddApexPostUtilities(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaintenanceModeOptions>(configuration.GetSection(MaintenanceModeOptions.SectionName));
        services.Configure<IpRestrictionOptions>(configuration.GetSection(IpRestrictionOptions.SectionName));
        services.Configure<IntegrationOptions>(configuration.GetSection(IntegrationOptions.SectionName));
        services.Configure<PerformanceOptions>(configuration.GetSection(PerformanceOptions.SectionName));
        services.Configure<SyncOptions>(configuration.GetSection(SyncOptions.SectionName));

        // IAuditStore (SQL, outside the request's unit of work) is registered by the persistence layer.
        services.AddSingleton<AuditActionFilter>();
        services.AddSingleton<PerformanceFilter>();
        services.AddSingleton<ValidationFilter>();
        services.AddSingleton<DomainExceptionFilter>();

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }
}
